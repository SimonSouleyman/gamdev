using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Drift.Bridge;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using Drift.Visuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.EditorTools.Playtest
{
    // Reusable play-test scenarios (menu Drift/Playtest or PlaytestRunner.Run from eval). Each one starts a fresh
    // run on a fixed seed, plays it with a HumanBot, records with PlaytestRecorder and writes
    // Playtests/<scenario>_<time>.json next to Assets so later versions can be compared.
    // Runs the owner's real save slot: back up LocalLow/.../Drift first (see CLAUDE.md).
    public static class PlaytestRunner
    {
        public class Options
        {
            public int seed = 4242;
            public bool direct = true;
            public bool mobilePipeline = true;
            public bool mute = true;
            public int targetFps = 0;
            public float minutes = 30f;
            public bool exitWhenDone;
            public string clip = "";
        }

        public static string Status { get; private set; } = "idle";
        public static bool Running => _script != null;
        public static PlaytestResult Current { get; private set; }
        public static string LastJson { get; private set; }
        public static string ShotDir { get; private set; }

        static IEnumerator _script;
        static readonly Stack<IEnumerator> _stack = new();
        static PlaytestRecorder _rec;
        static float _wait;
        static Func<bool> _until;
        static float _untilLeft;
        static Options _opt;
        static GameSession _session;
        static SessionScreens _screens;
        static IslandChaseCamera _chase;
        static WatchTools _watch;
        static HumanBot _bot;
        static bool _botOn;
        static RenderPipelineAsset _pipeline0;
        static float _volume0;
        static int _fps0, _vsync0;
        static int _seen0 = -1;
        static float _startReal;
        static string _pending;
        static Options _pendingOpt;

        public static readonly string[] Scenarios =
            { "cozy_observe", "adventure_good", "adventure_poor", "soak_cozy", "tutorial_cozy", "tutorial_adventure", "clips", "watch" };

        // ------------------------------------------------------------------ menu

        [MenuItem("Drift/Playtest/Cozy observation")] static void MCozy() => Queue("cozy_observe");
        [MenuItem("Drift/Playtest/Adventure (good bot)")] static void MAdvGood() => Queue("adventure_good");
        [MenuItem("Drift/Playtest/Adventure (poor bot)")] static void MAdvPoor() => Queue("adventure_poor");
        [MenuItem("Drift/Playtest/Soak 30 min (cozy)")] static void MSoak() => Queue("soak_cozy");
        [MenuItem("Drift/Playtest/Tutorial cozy")] static void MTutC() => Queue("tutorial_cozy");
        [MenuItem("Drift/Playtest/Tutorial adventure")] static void MTutA() => Queue("tutorial_adventure");
        [MenuItem("Drift/Playtest/Stop")] static void MStop() => Abort("stopped from the menu");

        static void Queue(string scenario)
        {
            var o = new Options { exitWhenDone = true };
            if (EditorApplication.isPlaying) { Run(scenario, o); return; }
            _pending = scenario;
            _pendingOpt = o;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && _pending != null)
            {
                var p = _pending;
                _pending = null;
                Run(p, _pendingOpt);
            }
            if (s == PlayModeStateChange.ExitingPlayMode && Running) Abort("play mode ended");
        }

        // ------------------------------------------------------------------ control

        public static bool Run(string scenario, Options opt = null)
        {
            if (!EditorApplication.isPlaying || Running || Array.IndexOf(Scenarios, scenario) < 0) { Status = "error: cannot run " + scenario; return false; }
            _opt = opt ?? new Options();
            // Leaving Play Mode ends the run (also when it was started from eval rather than the menu).
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.runInBackground = true;
            _session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
            _screens = UnityEngine.Object.FindAnyObjectByType<SessionScreens>();
            _chase = UnityEngine.Object.FindAnyObjectByType<IslandChaseCamera>();
            _watch = UnityEngine.Object.FindAnyObjectByType<WatchTools>();
            if (_session == null || _screens == null || _chase == null) { Status = "error: scene objects missing"; return false; }

            Current = new PlaytestResult
            {
                scenario = scenario,
                version = Application.version,
                started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                seed = _opt.seed,
                directSteering = _opt.direct,
                screenW = Screen.width,
                screenH = Screen.height,
            };
            ShotDir = Path.Combine(ProjectRoot, "Playtests", "shots", scenario + "_" + DateTime.Now.ToString("HHmmss"));
            Directory.CreateDirectory(ShotDir);
            _rec = new PlaytestRecorder(Current) { cam = _chase.GetComponent<Camera>() };
            _rec.Start();

            _volume0 = AudioListener.volume;
            if (_opt.mute) AudioListener.volume = 0f;
            _pipeline0 = QualitySettings.renderPipeline;
            if (_opt.mobilePipeline)
            {
                var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
                if (mobile != null) QualitySettings.renderPipeline = mobile;
            }
            Current.pipeline = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : "default";
            _fps0 = Application.targetFrameRate;
            _vsync0 = QualitySettings.vSyncCount;
            if (_opt.targetFps > 0) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = _opt.targetFps; }

            _bot = null;
            _botOn = false;
            SessionScreens.ScreenOverride = BotScreen;
            SessionScreens.MoveOverride = BotMove;
            Application.logMessageReceived += OnLog;
            TildaVoice.LineStarted += OnTilda;

            _script = Script(scenario);
            _stack.Clear();
            _stack.Push(_script);
            _wait = 0f;
            _until = null;
            _seen0 = -1;
            _startReal = Time.realtimeSinceStartup;
            Status = "running " + scenario;
            Application.onBeforeRender -= Tick;
            Application.onBeforeRender += Tick;
            return true;
        }

        public static void Abort(string why)
        {
            if (!Running) return;
            Current?.notes.Add("aborted: " + why);
            Finish();
        }

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        static void Finish()
        {
            Application.onBeforeRender -= Tick;
            _rec?.Stop();
            SessionScreens.ScreenOverride = null;
            SessionScreens.MoveOverride = null;
            Application.logMessageReceived -= OnLog;
            TildaVoice.LineStarted -= OnTilda;
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            AudioListener.volume = _volume0;
            QualitySettings.renderPipeline = _pipeline0;
            Application.targetFrameRate = _fps0;
            QualitySettings.vSyncCount = _vsync0;
            _script = null;
            _stack.Clear();
            if (Current != null)
            {
                Current.realSeconds = Time.realtimeSinceStartup - _startReal;
                Current.bot = _bot != null ? _bot.name : "none";
                string dir = Path.Combine(ProjectRoot, "Playtests");
                Directory.CreateDirectory(dir);
                LastJson = Path.Combine(dir, Current.scenario + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                File.WriteAllText(LastJson, JsonUtility.ToJson(Current, true));
            }
            Status = "done " + (Current != null ? Current.scenario : "") + " -> " + LastJson;
            if (_opt != null && _opt.exitWhenDone) EditorApplication.ExitPlaymode();
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (Current == null || Current.errors.Count >= 60) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Current.errors.Add($"{_rec?.time:0.0}s {type}: {msg} | {FirstLine(stack)}");
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int n = s.IndexOf('\n');
            return n < 0 ? s : s.Substring(0, n);
        }

        static void OnTilda(TildaLine line)
        {
            if (Current != null && line != null) Current.notes.Add($"{_rec?.time:0.0}s Tilda[{line.key}]: {line.text}");
        }

        static Vector2 BotScreen()
        {
            if (!_botOn || _bot == null || _rec == null) return Vector2.zero;
            return _bot.Screen(_rec.time, Time.deltaTime, _chase != null ? _chase.ViewYawDeg : 0f);
        }

        static Vector2 BotMove()
        {
            if (!_botOn || _bot == null || _rec == null || _rec.player == null) return Vector2.zero;
            Vector2 screen = _bot.Screen(_rec.time, Time.deltaTime, _chase != null ? _chase.ViewYawDeg : 0f);
            if (screen.sqrMagnitude < 1e-6f) return Vector2.zero;
            Vector2 world = TiltMath.ToWorld(screen, _chase != null ? _chase.SteerYawDeg : 0f);
            var p = _rec.player;
            return TiltMath.AsTurnThrottle(world, new Vector2(p.Forward.x, p.Forward.z));
        }

        static void Tick()
        {
            if (_script == null) return;
            try
            {
                _rec.Frame(Time.deltaTime, Time.unscaledDeltaTime);
                if (_rec.MemoryDue) _rec.SampleMemory(10f, _rec.result.memory.Count % 6 == 0);
                PollDiscovery();
                if (_until != null)
                {
                    _untilLeft -= Time.deltaTime;
                    if (!_until() && _untilLeft > 0f) return;
                    _until = null;
                }
                if (_wait > 0f) { _wait -= Time.deltaTime; if (_wait > 0f) return; }
                for (int guard = 0; guard < 64; guard++)
                {
                    var top = _stack.Peek();
                    if (!top.MoveNext())
                    {
                        _stack.Pop();
                        if (_stack.Count == 0) { Finish(); return; }
                        continue;
                    }
                    var y = top.Current;
                    if (y is IEnumerator sub) { _stack.Push(sub); continue; }
                    if (y is float f) _wait = f;
                    else if (y is Until u) { _until = u.cond; _untilLeft = u.timeout; }
                    return;
                }
            }
            catch (Exception e)
            {
                Current?.errors.Add("runner: " + e);
                Debug.LogException(e);
                Finish();
            }
        }

        static void PollDiscovery()
        {
            var j = _watch != null ? _watch.Journal : null;
            if (j == null) return;
            if (_seen0 < 0) { _seen0 = j.RunSeenCount; return; }
            if (j.RunSeenCount > _seen0)
            {
                for (int i = _seen0; i < j.RunSeenCount; i++)
                    _rec.Record("Discovery", _rec.player != null ? _rec.player.transform.position : Vector3.zero, true);
                _seen0 = j.RunSeenCount;
            }
        }

        public class Until
        {
            public Func<bool> cond;
            public float timeout;
            public Until(Func<bool> c, float t) { cond = c; timeout = t; }
        }

        // ------------------------------------------------------------------ helpers

        static void Shot(string name)
        {
            string path = Path.Combine(ShotDir, $"{Current.shots.Count:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Current.shots.Add(path);
        }

        static IEnumerator StartGame(GameMode mode)
        {
            if (_session.Current != GameSession.State.Title) { _session.ReturnToTitle(); yield return 0.5f; }
            SteerSettings.Direct = _opt.direct;
            Island.DirectionSteering = _opt.direct;
            _session.SelectMode(mode);
            _session.StartNewGame(_opt.seed);
            Current.mode = mode.ToString();
            yield return 0.2f;
            _rec.player = _session.player;
            yield return 1f;
        }

        static void Bot(HumanBot bot, Func<Vector2> plan)
        {
            _bot = bot;
            if (bot == null) { _botOn = false; return; }
            bot.plan = plan;
            bot.Reset(_opt.seed);
            _botOn = true;
        }

        static void HandsOff() => _botOn = false;

        static IEnumerator Script(string scenario)
        {
            switch (scenario)
            {
                case "cozy_observe": return CozyObserve();
                case "adventure_good": return Adventure(HumanBot.Good(), 480f);
                case "adventure_poor": return Adventure(HumanBot.Poor(), 360f);
                case "soak_cozy": return Soak();
                case "tutorial_cozy": return Tutorial(GameMode.Cozy);
                case "tutorial_adventure": return Tutorial(GameMode.Adventure);
                case "clips": return Clips();
                case "watch": return WatchDayNight();
                default: throw new ArgumentException("unknown scenario " + scenario);
            }
        }

        // ------------------------------------------------------------------ cozy

        static IEnumerator CozyObserve()
        {
            yield return StartGame(GameMode.Cozy);
            var player = _rec.player;
            float zoom0 = _chase.Zoom;
            Current.notes.Add($"default zoom {zoom0:0.00}, start land {player.LandArea:0}");

            _rec.BeginPhase("early_idle");
            HandsOff();
            yield return 45f; Shot("early_idle");
            yield return 45f;

            _rec.BeginPhase("grow");
            Bot(HumanBot.Casual(), HumanBot.Explorer(player));
            float targetArea = player.LandArea * 4f;
            yield return new Until(() => player.LandArea >= targetArea, 300f);
            Shot("grown");

            _rec.BeginPhase("late_idle");
            HandsOff();
            yield return 60f; Shot("late_idle");
            yield return 60f;

            foreach (float z in new[] { 0.15f, 0.3f, 0.6f, 1f, 2f })
            {
                _chase.SetZoom(z);
                yield return 2f;
                _rec.BeginPhase("zoom_" + z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                _rec.SnapshotView();
                Shot("zoom_" + z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                yield return 20f;
            }
            _chase.SetZoom(zoom0);

            var herds = player.GetComponent<IslandHerdSystem>();
            if (_watch != null && herds != null && herds.HerdCount > 0)
            {
                _rec.BeginPhase("watch_herd");
                _watch.FollowHerd(player, herds, herds.HerdCount / 2);
                yield return 20f; Shot("watch_20s");
                _rec.SnapshotView();
                yield return 40f; Shot("watch_60s");
                yield return 30f;
                _watch.ReturnToIsland();
            }
            else Current.notes.Add("no herd to watch");
            yield return 2f;
            _rec.EndPhase();
        }

        // Following a herd by day and by night, with the LifeDirector's nudges logged every 10 s.
        static IEnumerator WatchDayNight()
        {
            yield return StartGame(GameMode.Cozy);
            var player = _rec.player;
            Bot(HumanBot.Casual(), HumanBot.Explorer(player));
            float target = player.LandArea * 4f;
            yield return new Until(() => player.LandArea >= target, 240f);
            HandsOff();
            var cycle = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
            var director = UnityEngine.Object.FindAnyObjectByType<LifeDirector>();
            var herds = player.GetComponent<IslandHerdSystem>();
            foreach (var (name, tod) in new[] { ("watch_day", 0.32f), ("watch_night", 0.82f) })
            {
                if (cycle != null) cycle.timeOfDay = tod;
                yield return 3f;
                if (_watch == null || herds == null || herds.HerdCount == 0) { Current.notes.Add("no herd"); yield break; }
                int best = 0;
                for (int h = 1; h < herds.HerdCount; h++) if (herds.HerdSize(h) > herds.HerdSize(best)) best = h;
                _watch.FollowHerd(player, herds, best);
                yield return 3f;
                _rec.BeginPhase(name);
                _rec.SnapshotView();
                int n0 = director != null ? director.Nudges : 0;
                for (int i = 0; i < 9; i++)
                {
                    yield return 10f;
                    if (i == 2 || i == 6) Shot($"{name}_{i * 10 + 10}s");
                    if (director != null)
                        Current.notes.Add($"{name} {i * 10 + 10}s: nudges {director.Nudges - n0}, last {director.LastNudge}, since {director.SinceNoticed:0.0}, running {director.Running}, night {director.Night}, herd {herds.HerdKind(best)}");
                }
                _watch.ReturnToIsland();
                _rec.EndPhase();
            }
        }

        // ------------------------------------------------------------------ adventure

        static IEnumerator Adventure(HumanBot bot, float maxSeconds)
        {
            yield return StartGame(GameMode.Adventure);
            var player = _rec.player;
            Bot(bot, HumanBot.Racer(player, bot, UnityEngine.Object.FindAnyObjectByType<ShipSystem>()));
            _rec.BeginPhase("race");
            float t = 0f, shotAt = 5f;
            int lastLevel = 0;
            while (t < maxSeconds && !_session.IsGameOver && _session.Current == GameSession.State.Playing)
            {
                var ring = RingWorld.Active;
                int lvl = ring != null ? ring.Level : 1;
                if (lvl != lastLevel)
                {
                    lastLevel = lvl;
                    _rec.BeginLevel(lvl);
                    shotAt = t + 4f;
                }
                if (t >= shotAt) { Shot($"L{lvl}_{t:000}s"); shotAt = t + 30f; }
                t += Time.deltaTime;
                yield return null;
            }
            Current.notes.Add($"race over after {t:0.0}s, level {lastLevel}, state {_session.Current}, hits {AdventureRunStats.Hits}, dodges {AdventureRunStats.Dodges}, flotsam {AdventureRunStats.Flotsam}");
            _rec.EndLevel();
            if (_session.IsGameOver) { yield return 1.5f; Shot("game_over"); yield return 0.5f; }
            _rec.EndPhase();
        }

        // ------------------------------------------------------------------ soak

        static IEnumerator Soak()
        {
            yield return StartGame(GameMode.Cozy);
            var player = _rec.player;
            _rec.BeginPhase("soak_grow");
            Bot(HumanBot.Casual(), HumanBot.Explorer(player));
            yield return 180f;
            HandsOff();
            int blocks = Mathf.Max(1, Mathf.RoundToInt((_opt.minutes * 60f - 180f) / 300f));
            for (int b = 0; b < blocks; b++)
            {
                _rec.BeginPhase($"soak_{b:00}");
                yield return 300f;
                Shot($"soak_{b:00}");
            }
            _rec.EndPhase();
        }

        // ------------------------------------------------------------------ tutorials

        static IEnumerator Tutorial(GameMode mode)
        {
            yield return StartGame(mode);
            var player = _rec.player;
            if (mode == GameMode.Cozy)
            {
                var guide = UnityEngine.Object.FindAnyObjectByType<TutorialGuide>();
                if (guide != null) guide.RequestReplay();
                Bot(HumanBot.Casual(), HumanBot.Explorer(player));
            }
            else
            {
                AdventureTutorialGuide.RequestReplay();
                var bot = HumanBot.Casual();
                Bot(bot, HumanBot.Racer(player, bot, UnityEngine.Object.FindAnyObjectByType<ShipSystem>()));
            }
            _rec.BeginPhase("tutorial");
            var cozyGuide = UnityEngine.Object.FindAnyObjectByType<TutorialGuide>();
            var advGuide = UnityEngine.Object.FindAnyObjectByType<AdventureTutorialGuide>();
            // A reader: every bubble is read for ~4 s (screenshot), then "Weiter" is tapped.
            float t = 0f, readFor = 0f;
            int taps = 0, shots = 0;
            while (t < 180f && _session.Current == GameSession.State.Playing)
            {
                bool bubble = mode == GameMode.Cozy ? cozyGuide != null && cozyGuide.BubbleVisible : advGuide != null && advGuide.BubbleVisible;
                if (bubble)
                {
                    readFor += 0.5f;
                    if (readFor >= 4f)
                    {
                        Shot($"tut_tap{taps:00}_{t:000}s");
                        if (mode == GameMode.Cozy) cozyGuide.Continue(); else advGuide.Continue();
                        Current.notes.Add($"{_rec.time:0.0}s tutorial tap {taps}");
                        taps++;
                        readFor = 0f;
                    }
                }
                else readFor = 0f;
                if (t >= shots * 10f) { Shot($"tut_{t:000}s"); shots++; }
                yield return 0.5f;
                t += 0.5f;
            }
            Current.notes.Add($"tutorial: {taps} taps, bubble still visible at the end: {(mode == GameMode.Cozy ? cozyGuide != null && cozyGuide.BubbleVisible : advGuide != null && advGuide.BubbleVisible)}");
            _rec.EndPhase();
        }

        // ------------------------------------------------------------------ clips

        // Frame sequences at a fixed 12.5 fps of game time (captureFramerate): cozy herd watch + fly by, and
        // the race at level 1 and later. Filmstrips are built afterwards with PlaytestCapture.Filmstrip.
        static IEnumerator Clips()
        {
            yield return StartGame(GameMode.Cozy);
            var player = _rec.player;
            Bot(HumanBot.Casual(), HumanBot.Explorer(player));
            float target = player.LandArea * 3f;
            yield return new Until(() => player.LandArea >= target, 240f);
            HandsOff();
            yield return 20f;
            yield return Clip("cozy_island", 8f);
            var herds = player.GetComponent<IslandHerdSystem>();
            if (_watch != null && herds != null && herds.HerdCount > 0)
            {
                _watch.FollowHerd(player, herds, herds.HerdCount / 2);
                yield return 8f;
                yield return Clip("cozy_watch", 8f);
                _watch.ReturnToIsland();
                yield return 2f;
            }
            Bot(HumanBot.Casual(), HumanBot.Explorer(player));
            yield return 5f;
            yield return Clip("cozy_drive", 6f);

            yield return StartGame(GameMode.Adventure);
            player = _rec.player;
            var bot = HumanBot.Good();
            Bot(bot, HumanBot.Racer(player, bot, UnityEngine.Object.FindAnyObjectByType<ShipSystem>()));
            yield return 12f;
            yield return Clip("adv_level1", 6f);
            yield return new Until(() => RingWorld.Active != null && RingWorld.Active.Level >= 3, 200f);
            yield return 5f;
            if (_session.Current == GameSession.State.Playing) yield return Clip("adv_level3", 6f);
        }

        static IEnumerator Clip(string name, float seconds)
        {
            const int Fps = 12;
            string dir = Path.Combine(ShotDir, "clip_" + name);
            Directory.CreateDirectory(dir);
            Time.captureFramerate = Fps;
            int frames = Mathf.RoundToInt(seconds * Fps);
            for (int i = 0; i < frames; i++)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"f{i:000}.png"));
                yield return null;
            }
            Time.captureFramerate = 0;
            Current.shots.Add(dir);
            yield return 0.5f;
        }
    }
}
