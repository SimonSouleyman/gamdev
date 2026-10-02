using Drift.Core;
using Drift.Islands;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Drift.Bridge
{
    [ExecuteAlways]
    public class WorldHud : MonoBehaviour
    {
        const string CanvasName = "WorldHudCanvas";
        const string ExplainSeenKey = "drift_hud_explained";

        public Island player;
        public WorldStreamer streamer;
        public GameSession session;
        public IslandChaseCamera chaseCamera;
        public int mapPixels = 128;
        public float refresh = 0.3f;
        // Edit mode only: which mode's HUD the Game view shows (Auto = GameModes.Current).
        public EditorMode editorMode = EditorMode.Auto;
        [Tooltip("Nur im Editor: zeigt die Erklärkarte unter der Gemütlich-Leiste in der Spielansicht.")]
        public bool editorExplain;
        [Tooltip("Sekunden, die die Erklärkarte stehen bleibt, bevor sie sich selbst schließt.")]
        public float explainSeconds = 10f;
        [Tooltip("Nur im Editor: zeigt den Startsatz unter der Abenteuer-Leiste in der Spielansicht.")]
        public bool editorStartLine;

        public enum EditorMode { Auto, Cozy, Adventure }

        // Minimap edge per mode: the race looks up the screen, so its map and hint stay small.
        const float MapSize = 340f, MapSizeAdventure = 240f;
        const float HintWide = 760f, HintNarrow = 610f, HintAdventure = 520f;
        // Top panel: 820 wide and left-anchored so the round 120-unit buttons on the right stay clear in portrait.
        const float Inner = 740f, Pad = 40f, PanelWidth = Inner + 2f * Pad;
        const float CozyHeight = 232f, AdventureHeight = 198f;
        const float LabelColumn = 138f, ValueColumn = 196f, LevelColumn = 128f;
        // "5 von 20 Inseln · 3 Vulkane" needs more room than the plain count: the world bar gives way up to this.
        const float WorldValueMax = 380f;
        const float StartLineHeight = 128f, StartLineFadeIn = 0.35f;

        Canvas _canvas;
        RectTransform _root, _hintRect, _top, _cozy, _adventure, _explain;
        int _hintLayout = -1;
        WatchTools _watch;
        TiltSteering _tilt;
        TutorialGuide _tutorial;
        Image _topBody;
        Button _topButton;
        // Gemütlich
        Text _areaText, _cozyStatusText, _islandsText, _formText, _milestoneText, _hintText;
        // The season as a small badge left of the island's state in the head row (cozy only, hidden without a year).
        Image _seasonPill;
        Text _seasonText;
        int _lastSeason = int.MinValue;
        UiBar _worldBar, _cozyBuoyBar;
        // Abenteuer
        Text _timerText, _bestText, _boostText, _levelText, _advBuoyLabel;
        UiBar _advBuoyBar;
        // Abenteuer "Schwung": a bar under the buoyancy, filled by chained boosts and surfing.
        UiBar _momentumBar;
        Text _momentumLabel;
        int _momentumLit = -1;
        // Abenteuer: one sentence under the strip while the race waits on the start line.
        CanvasGroup _startLine;
        float _startLineAlpha = -1f;
        Image _advTrack;
        UiBar _buoyBar;
        string _lastMilestone = "";
        RectTransform _mapFrame;
        GameMode _shownMode = (GameMode)(-1);
        int _lastTimer = int.MinValue, _lastBest = int.MinValue, _lastBoost = int.MinValue;
        RawImage _map;
        Texture2D _mapTex;
        Color32[] _pixels;
        float _timer;
        float _lookupTimer;
        int _lastArea = int.MinValue, _lastIslands = int.MinValue, _lastLeft = int.MinValue, _lastVolcanoes = int.MinValue;
        int _lastSinkState = -1, _lastHintState = -1, _lastForm = -1, _lastHeavy = -1;
        float _explainLeft;
        bool _explainShown;
        int _explainSeen = -1;

        static readonly Color32 MapBg = new Color32(18, 52, 72, 255);
        static readonly Color32 MapIsland = new Color32(140, 222, 176, 255);
        static readonly Color32 MapPlayer = new Color32(248, 217, 158, 255);

        // Targets of the tutorial's highlight ring.
        public RectTransform BuoyancyRect => _buoyBar != null ? _buoyBar.Root : null;
        public RectTransform FormRect => _formText != null ? _formText.rectTransform : null;
        public RectTransform DistanceRect => _timerText != null ? _timerText.rectTransform : null;
        // Canvas units from the top of the safe area down to the lower edge of the top panel in the current mode
        // (for toasts that sit under it).
        public float PanelBottom => UiStyle.Margin + (Mode == GameMode.Adventure ? AdventureHeight : CozyHeight);
        public bool ExplainOpen => _explainShown;

        void OnEnable()
        {
            Build();
            _lastArea = _lastIslands = _lastLeft = _lastVolcanoes = _lastSeason = int.MinValue;
            _startLineAlpha = -1f;
            _momentumLit = -1;
            _lastSinkState = _lastHintState = _lastForm = _lastHeavy = -1;
            _lastTimer = _lastBest = _lastBoost = int.MinValue;
            _shownMode = (GameMode)(-1);
            _timer = refresh;
            _explainShown = false;
            Island.Bumped -= OnBumped;
            Island.Bumped += OnBumped;
            RingWorld.Dodged -= OnDodged;
            RingWorld.Dodged += OnDodged;
            RingWorld.RaceStarted -= OnRaceStarted;
            RingWorld.RaceStarted += OnRaceStarted;
        }

        // Adventure calls: a hit costs buoyancy (coral), a close pass is worth a cheer (mint).
        const float CallSeconds = 1.6f;
        float _callLeft;
        string _callText = "";
        Color _callColor = Color.white;

        void Call(string text, Color color)
        {
            _callText = text;
            _callColor = color;
            _callLeft = CallSeconds;
            _lastBoost = int.MinValue;
        }

        void OnBumped(Island hit, Island obstacle, Vector2 contact, float strength)
        {
            if (hit == null || hit != player) return;
            Call(ModeTexts.HitLabel(hit.LastHitLoss), UiStyle.Coral);
        }

        void OnDodged(Island island)
        {
            if (_callLeft > 0f && _callColor == UiStyle.Coral) return;
            Call(ModeTexts.DodgeLabel, UiStyle.Mint);
        }

        // The start line lets go (after the briefing and the breath before the race).
        void OnRaceStarted() => Call(ModeTexts.GoLabel, UiStyle.Mint);

        void OnDisable()
        {
            Island.Bumped -= OnBumped;
            RingWorld.Dodged -= OnDodged;
            RingWorld.RaceStarted -= OnRaceStarted;
            if (_mapTex != null)
            {
                if (Application.isPlaying) Destroy(_mapTex);
                else DestroyImmediate(_mapTex);
            }
        }

        void Update()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            // Scene-wide searches are only retried once a second so a legitimately absent reference
            // does not cost a FindFirstObjectByType every frame.
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer <= 0f && (streamer == null || session == null || chaseCamera == null || _tutorial == null))
            {
                _lookupTimer = 1f;
                if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
                if (session == null) session = FindAnyObjectByType<GameSession>();
                if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
                if (_tutorial == null) _tutorial = FindAnyObjectByType<TutorialGuide>();
            }
            if (_canvas == null || player == null) return;

            bool show = (!Application.isPlaying || session == null
                || session.Current == GameSession.State.Playing || session.Current == GameSession.State.Paused) && !WatchTools.HudHidden;
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;

            var mode = Mode;
            bool adventure = mode == GameMode.Adventure;
            if (mode != _shownMode) ApplyMode(mode);

            float buoy = player.Buoyancy;
            _buoyBar.Set(buoy);
            Color buoyColor;
            if (adventure)
            {
                // Abenteuer: the danger meter, coral and pulsing when the next flotsam is urgent.
                buoyColor = Color.Lerp(UiStyle.Coral, UiStyle.Sky, Mathf.Clamp01((buoy - 0.25f) / 0.6f));
                bool low = buoy < ModeTexts.LowBuoyancy;
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                if (low) buoyColor = Color.Lerp(buoyColor, UiStyle.Cream, pulse);
                // The empty part of the track glows coral as well, so a nearly empty bar still reads at a glance.
                if (low) UiStyle.Tint(_advTrack, Color.Lerp(UiStyle.Track, UiStyle.WithAlpha(UiStyle.Coral, 0.55f), pulse));
                else if (_advTrack.color != UiStyle.Track) _advTrack.color = UiStyle.Track;
                int heavy = buoy < ModeTexts.HeavyBuoyancy ? 1 : 0;
                if (heavy != _lastHeavy)
                {
                    _lastHeavy = heavy;
                    _advBuoyLabel.color = heavy == 1 ? UiStyle.Coral : UiStyle.CreamSoft;
                }
            }
            else buoyColor = Color.Lerp(UiStyle.Sand, UiStyle.Sky, Mathf.Clamp01((buoy - 0.3f) / 0.5f));
            _buoyBar.Tint(buoyColor);
            _buoyBar.Animate(Time.unscaledTime);
            if (adventure) UpdateAdventure(buoy);
            else UpdateExplain();

            _timer += Time.unscaledDeltaTime;
            if (_timer < refresh) return;
            _timer = 0f;

            if (!adventure) UpdateCozy();
            else
            {
                var ringWorld = RingWorld.Active;
                int level = ringWorld != null ? ringWorld.Level : 1;
                if (level != _lastForm)
                {
                    _lastForm = level;
                    _levelText.text = ModeTexts.LevelLabel(level);
                }
            }
            if (_watch == null && _lookupTimer <= 0f) _watch = FindAnyObjectByType<WatchTools>();
            bool following = _watch != null && _watch.Following;
            bool flyOver = PangaeaFinale.AnyFlyingOver;
            if (_tilt == null && _lookupTimer <= 0f) _tilt = FindAnyObjectByType<TiltSteering>();
            bool tilting = _tilt != null && _tilt.Active;
            int hintState = (InputMode.TouchPreferred ? 1 : 0) + (following ? 2 : 0)
                + (adventure ? 4 : 0) + (Island.DirectionSteering ? 8 : 0) + (flyOver ? 16 : 0) + (tilting ? 32 : 0);
            if (hintState != _lastHintState)
            {
                _lastHintState = hintState;
                FillHint();
                // Following: the watch tools show the orbit controls under the herd chip already; over the finished
                // Pangäa the finale's banner explains the map gestures and there is no stick to hint at.
                if (_hintRect != null) _hintRect.gameObject.SetActive(!following && !flyOver);
            }
            if (!adventure) DrawMap();
            else
            {
                var ring = RingWorld.Active;
                bool ringMap = ring != null && (ring.IsApplied || !Application.isPlaying);
                if (_mapFrame != null && _mapFrame.gameObject.activeSelf != ringMap) _mapFrame.gameObject.SetActive(ringMap);
                if (ringMap) DrawRingMap(ring.Geometry);
            }
        }

        GameMode Mode => !Application.isPlaying && editorMode != EditorMode.Auto
            ? (editorMode == EditorMode.Adventure ? GameMode.Adventure : GameMode.Cozy)
            : GameModes.Current;

        // Gemütlich: land mass with what it is doing, a labelled world bar ("x von N Inseln"), a labelled buoyancy
        // bar with the island's form, the next milestone and the minimap. Abenteuer: one strip - distance, the race
        // calls and the record on top, the buoyancy bar with the level and the momentum bar below - and the ring as a
        // small map.
        void ApplyMode(GameMode mode)
        {
            _shownMode = mode;
            bool adventure = mode == GameMode.Adventure;
            if (_cozy.gameObject.activeSelf == adventure) _cozy.gameObject.SetActive(!adventure);
            if (_adventure.gameObject.activeSelf != adventure) _adventure.gameObject.SetActive(adventure);
            _buoyBar = adventure ? _advBuoyBar : _cozyBuoyBar;
            float height = adventure ? AdventureHeight : CozyHeight;
            _top.sizeDelta = new Vector2(PanelWidth, height);
            // Only the cozy panel explains itself on a tap; the race strip never takes a touch from the stick.
            _topBody.raycastTarget = !adventure;
            _topButton.enabled = !adventure;
            if (adventure) HideExplain();
            float map = adventure ? MapSizeAdventure : MapSize;
            if (_mapFrame != null)
            {
                _mapFrame.sizeDelta = new Vector2(map, map);
                if (!_mapFrame.gameObject.activeSelf) _mapFrame.gameObject.SetActive(true);
            }
            _hintLayout = -1;
            PlaceHint();
            _ringBgBuilt = false;
            _lastArea = _lastIslands = _lastLeft = _lastVolcanoes = _lastTimer = _lastBest = _lastBoost = _lastSeason = int.MinValue;
            _lastSinkState = _lastForm = _lastHeavy = _momentumLit = -1;
            _startLineAlpha = -1f;
            _lastMilestone = "";
            _timer = refresh;
        }

        static void SetActive(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        bool CozySinking => !Application.isPlaying || (player.sinkEnabled && !player.SinkResting);

        void UpdateCozy()
        {
            float remaining = 0f;
            int left = 0, total = 0;
            if (streamer != null)
            {
                streamer.Progress(out remaining, out left);
                total = streamer.WorldIslandCount;
            }
            float area = player.LandArea;
            float sum = area + remaining;
            float pct = sum > 0f ? area / sum : 1f;

            int areaI = Mathf.RoundToInt(area);
            if (areaI != _lastArea)
            {
                _lastArea = areaI;
                _areaText.text = "Landmasse " + areaI;
            }
            _worldBar.Set(pct);
            // Volcanoes are no world slots, yet every merged one counts towards the next milestone; listed apart, so
            // the milestone line running ahead of "x von N Inseln" does not look like a miscount.
            int volcanoes = session != null ? session.Stats.volcanoesAbsorbed : 0;
            if (total != _lastIslands || left != _lastLeft || volcanoes != _lastVolcanoes)
            {
                _lastIslands = total;
                _lastLeft = left;
                _lastVolcanoes = volcanoes;
                _islandsText.text = ModeTexts.WorldProgressLabel(total - left, total, volcanoes);
                FitWorldRow();
            }
            int sinkState = CozySinking ? 1 : 0;
            int form = player.Compactness >= 0.475f ? 1 : 0;
            bool head = false;
            int season = SeasonIndex(LifeEnvironment.Season);
            if (season != _lastSeason)
            {
                _lastSeason = season;
                head = true;
                if (_seasonText != null)
                {
                    _seasonText.text = SeasonLabel(LifeEnvironment.Season);
                    _seasonText.color = SeasonColor(season);
                }
                SetActive(_seasonPill, season >= 0);
            }
            if (sinkState != _lastSinkState || form != _lastForm)
            {
                head = true;
                _lastSinkState = sinkState;
                _lastForm = form;
                _cozyStatusText.text = ModeTexts.CozySinkStatus(sinkState == 1);
                _cozyStatusText.color = sinkState == 1 ? UiStyle.Sand : UiStyle.Mint;
                _formText.text = ModeTexts.FormLabel(player.Compactness);
                _formText.color = form == 1 ? UiStyle.CreamSoft : UiStyle.Sand;
            }
            if (head) FitHeadRow();
            // The next milestone is the reason to look forward to the next merge (Leuchtturm, Hafen, ...).
            string next = session != null ? Drift.SaveSystem.Milestones.NextText(session.Stats.islandsAbsorbed) : "";
            if (next != _lastMilestone)
            {
                _lastMilestone = next;
                if (_milestoneText != null) _milestoneText.text = next;
            }
        }

        // 0 Frühling, 1 Sommer, 2 Herbst, 3 Winter around the year's keys (IslandLifeSystem: 0 spring, 0.25 summer,
        // 0.5 autumn, 0.75 the cold end); -1 without a year (no SeasonProvider).
        // TODO(v0.6.8 integration): use the seasons layer's own helper here once it exists, so the badge and the
        // herds' winter huddle agree on when winter starts.
        // Fixed quarters, the same boundaries as the herds' IslandHerdSystem.SeasonOf (the huddle starts when the badge says Winter).
        public static int SeasonIndex(float season) => season < 0f || float.IsNaN(season) ? -1 : Mathf.FloorToInt(Mathf.Repeat(season, 1f) * 4f) % 4;

        public static string SeasonLabel(float season)
        {
            switch (SeasonIndex(season))
            {
                case 0: return "Frühling";
                case 1: return "Sommer";
                case 2: return "Herbst";
                case 3: return "Winter";
                default: return "";
            }
        }

        static Color SeasonColor(int season) =>
            season == 0 ? UiStyle.Mint : season == 1 ? UiStyle.Sand : season == 2 ? UiStyle.Coral : UiStyle.Sky;

        public string SeasonShown => _seasonPill != null && _seasonPill.gameObject.activeSelf && _seasonText != null ? _seasonText.text : "";

        // Head row, right to left: the "?", the island's state, the season badge; "Landmasse" gets what is left (and
        // shrinks a long number a little rather than run under the badge).
        void FitHeadRow()
        {
            if (_areaText == null || _cozyStatusText == null) return;
            float right = 52f + Mathf.Min(Mathf.Ceil(_cozyStatusText.preferredWidth), 280f) + 14f;
            if (_seasonPill != null && _seasonPill.gameObject.activeSelf)
            {
                float w = Mathf.Ceil(_seasonText.preferredWidth) + 28f;
                _seasonPill.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-right, 0f), new Vector2(w, 40f));
                right += w + 14f;
            }
            float area = Mathf.Clamp(Inner - right, 220f, 400f);
            var rt = _areaText.rectTransform;
            if (Mathf.Abs(rt.sizeDelta.x - area) > 0.5f) rt.sizeDelta = new Vector2(area, rt.sizeDelta.y);
        }

        // The value column of the world row grows with its text (up to WorldValueMax) and the bar gives way, so the
        // volcano count stays at full size; without volcanoes the row keeps the buoyancy row's columns.
        void FitWorldRow()
        {
            if (_islandsText == null || _worldBar == null) return;
            float column = Mathf.Clamp(Mathf.Ceil(_islandsText.preferredWidth) + 16f, ValueColumn, WorldValueMax);
            var rt = _islandsText.rectTransform;
            if (Mathf.Abs(rt.sizeDelta.x - (column - 16f)) > 0.5f) rt.sizeDelta = new Vector2(column - 16f, rt.sizeDelta.y);
            var bar = _worldBar.Root;
            float width = Inner - LabelColumn - column;
            if (Mathf.Abs(bar.sizeDelta.x - width) > 0.5f) bar.sizeDelta = new Vector2(width, bar.sizeDelta.y);
        }

        // ---------------------------------------------------------------- cozy explanation card

        // Once per install it opens by itself the first time the island is seen sinking (not while Tilda's
        // tutorial talks about the same bars); afterwards a tap on the panel opens and closes it.
        void UpdateExplain()
        {
            if (!Application.isPlaying)
            {
                if (editorExplain != _explainShown) SetExplain(editorExplain);
                return;
            }
            bool playing = session == null || session.Current == GameSession.State.Playing;
            if (_explainShown)
            {
                _explainLeft -= Time.unscaledDeltaTime;
                if (!playing || _explainLeft <= 0f) HideExplain();
                return;
            }
            if (!playing || !CozySinking || ExplainSeen || (_tutorial != null && _tutorial.ReplayPending)) return;
            MarkExplainSeen();
            ShowExplain();
        }

        bool ExplainSeen
        {
            get
            {
                if (_explainSeen < 0) _explainSeen = PlayerPrefs.GetInt(ExplainSeenKey, 0);
                return _explainSeen == 1;
            }
        }

        void MarkExplainSeen()
        {
            _explainSeen = 1;
            PlayerPrefs.SetInt(ExplainSeenKey, 1);
            PlayerPrefs.Save();
        }

        public void ToggleExplain()
        {
            if (_explainShown) HideExplain();
            else
            {
                if (Application.isPlaying && !ExplainSeen) MarkExplainSeen();
                ShowExplain();
            }
        }

        public void ShowExplain()
        {
            _explainLeft = Mathf.Max(2f, explainSeconds);
            SetExplain(true);
        }

        public void HideExplain() => SetExplain(false);

        void SetExplain(bool on)
        {
            _explainShown = on;
            if (_explain != null && _explain.gameObject.activeSelf != on) _explain.gameObject.SetActive(on);
        }

        // Every frame, but the strings only change with a whole metre (distance, record) or with the calls.
        void UpdateAdventure(float buoy)
        {
            bool live = Application.isPlaying && session != null;
            float metres = live ? session.Stats.distance : 1234f;
            float best = live ? BestDistances.Get(GameMode.Adventure) : 3456f;
            int mi = BestDistances.Metres(metres);
            if (mi != _lastTimer)
            {
                _lastTimer = mi;
                _timerText.text = BestDistances.Format(metres);
            }
            // Past the old record the line turns into a cheer.
            int bi = best > 0f && metres > best ? -2 : BestDistances.Metres(best);
            if (bi != _lastBest)
            {
                _lastBest = bi;
                _bestText.text = bi == -2 ? ModeTexts.NewRecordLabel : best > 0f ? ModeTexts.RecordLabel(best) : ModeTexts.FirstRunLabel;
                _bestText.color = bi == -2 ? UiStyle.Mint : UiStyle.Muted;
            }
            if (_momentumBar != null)
            {
                float mom = live ? player.Momentum : 0.55f;
                _momentumBar.Set(mom);
                Color mc = Color.Lerp(UiStyle.Sand, UiStyle.Mint, Mathf.Clamp01(mom * 1.25f));
                if (mom > 0.9f) mc = Color.Lerp(mc, UiStyle.Cream, 0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f));
                _momentumBar.Tint(mc);
                int lit = mom > 0.05f ? 1 : 0;
                if (lit != _momentumLit)
                {
                    _momentumLit = lit;
                    _momentumLabel.color = lit == 1 ? UiStyle.CreamSoft : UiStyle.Muted;
                }
            }
            // One slot for the calls of the race, loudest first: the rim of the band, a hit or a dodge just now,
            // the flotsam boost, the empty bar, surfing.
            if (_callLeft > 0f) _callLeft -= Time.unscaledDeltaTime;
            // The tail of a boost (x1.0 after rounding) is no call any more.
            bool boosting = live ? player.Boosting && player.BoostFactor >= 1.05f : editorMode == EditorMode.Adventure;
            float factor = live ? player.BoostFactor : 1.6f;
            bool surfing = live && player.SurfStrength > 0.35f;
            bool low = live && buoy < ModeTexts.LowBuoyancy;
            // Near the rim of the band: a short note that the world ends there, not an alarm - nothing can happen.
            bool atEdge = live && player.EdgeWarning > 0.6f;
            int state = atEdge ? 4 : _callLeft > 0f ? 3 : boosting ? 1 : low ? 5 : surfing ? 2 : 0;
            int boostKey = state == 0 ? 0 : state == 4 ? 4000 : state == 5 ? 5000 : state == 3 ? 3000 + _callText.Length : state * 1000 + Mathf.RoundToInt(factor * 10f);
            if (boostKey != _lastBoost)
            {
                _lastBoost = boostKey;
                SetActive(_boostText, state != 0);
                if (state == 4) _boostText.text = ModeTexts.EdgeLabel;
                else if (state == 3) _boostText.text = _callText;
                else if (state == 1) _boostText.text = ModeTexts.BoostLabel(factor);
                else if (state == 5) _boostText.text = ModeTexts.LowBuoyancyCall;
                else if (state == 2) _boostText.text = ModeTexts.SurfLabel;
            }
            // The pulse goes through the renderer's alpha: a Graphic.color write would regenerate the text mesh every frame.
            float t = Time.unscaledTime;
            if (state == 4) PulseCall(UiStyle.Sand, 0.7f + 0.3f * Mathf.Sin(t * 6f));
            else if (state == 3) PulseCall(_callColor, 0.7f + 0.3f * Mathf.Sin(t * 12f));
            else if (state == 1) PulseCall(UiStyle.Mint, 0.75f + 0.25f * Mathf.Sin(t * 8f));
            else if (state == 5) PulseCall(UiStyle.Coral, 0.75f + 0.25f * Mathf.Sin(t * 5f));
            else if (state == 2) PulseCall(UiStyle.Sky, 0.85f);
            UpdateStartLine(live);
        }

        void PulseCall(Color color, float alpha)
        {
            UiStyle.Tint(_boostText, UiStyle.WithAlpha(color, 1f));
            _boostText.canvasRenderer.SetAlpha(alpha);
        }

        // Shown while the race waits on the start line after the briefing (Tilda explains it herself while she talks),
        // then gone with the "Los!" call: it fades over the same CallSeconds.
        void UpdateStartLine(bool live)
        {
            if (_startLine == null) return;
            bool waiting = live ? RingWorld.StartHeld && !session.StartHold : editorStartLine;
            float a = _startLineAlpha < 0f ? 0f : _startLineAlpha;
            if (!live) a = waiting ? 1f : 0f;
            else if (waiting) a = Mathf.Min(1f, a + Time.unscaledDeltaTime / StartLineFadeIn);
            else a = Mathf.Max(0f, a - Time.unscaledDeltaTime / CallSeconds);
            if (a == _startLineAlpha) return;
            _startLineAlpha = a;
            SetActive(_startLine, a > 0f);
            _startLine.alpha = a;
        }

        void DrawMap()
        {
            if (_mapTex == null || streamer == null || streamer.WorldSize <= 0f) return;
            int n = mapPixels;
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = MapBg;

            float w = streamer.WorldSize;
            // Storms first, under the islands: a dark patch that is seen coming long before the clouds are.
            for (int k = 0; k < Drift.Visuals.StormVisuals.StormCount; k++)
            {
                var st = Drift.Visuals.StormVisuals.StormAt(k);
                int sr = Mathf.Clamp(Mathf.RoundToInt(st.z / w * n * 1.4f), 5, n / 3);
                Blend(new Vector2(st.x, st.y), w, n, sr, MapStorm, 0.9f * Mathf.Clamp01(st.w));
            }
            var slots = streamer.WorldSlots();
            // Without the Leuchtturm (milestone at 3 islands) the map only shows what is near; with it, everything.
            float range = Drift.SaveSystem.Milestones.MapRange;
            Vector2 me = player.PlanarPosition;
            for (int k = 0; k < slots.Count; k++)
            {
                var s = slots[k];
                if (s.consumed) continue;
                if (!float.IsPositiveInfinity(range))
                {
                    float dx = Mathf.Abs(Mod(s.pos.x - me.x + w * 0.5f, w) - w * 0.5f);
                    float dy = Mathf.Abs(Mod(s.pos.y - me.y + w * 0.5f, w) - w * 0.5f);
                    if (dx * dx + dy * dy > range * range) continue;
                }
                int r = Mathf.Clamp(Mathf.RoundToInt(s.radius / w * n * 1.6f), 1, 4);
                Plot(s.pos, w, n, r, MapIsland);
            }
            int pr = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(player.LandArea) / w * n * 1.3f), 2, 8);
            Plot(player.PlanarPosition, w, n, pr, MapPlayer);

            _mapTex.SetPixels32(_pixels);
            _mapTex.Apply(false);
        }

        // Abenteuer: the ring seen from above as an annulus; the angle is the position along the ring, the radius the
        // position across the band. The water band is drawn once into _ringBg and copied each refresh.
        const float RingInner = 0.30f, RingOuter = 0.47f;
        Color32[] _ringBg;
        bool _ringBgBuilt;
        static readonly Color32 MapClear = new Color32(0, 0, 0, 0);
        static readonly Color32 MapRim = new Color32(210, 196, 160, 255);

        void DrawRingMap(RingGeometry g)
        {
            if (_mapTex == null || g.circumference <= 0f) return;
            int n = mapPixels;
            if (!_ringBgBuilt || _ringBg == null || _ringBg.Length != _pixels.Length)
            {
                _ringBg = new Color32[_pixels.Length];
                float c = (n - 1) * 0.5f, rim = 1.2f / n;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / n;
                        _ringBg[y * n + x] = r < RingInner - rim || r > RingOuter + rim ? MapClear
                            : r < RingInner || r > RingOuter ? MapRim : MapBg;
                    }
                _ringBgBuilt = true;
            }
            System.Array.Copy(_ringBg, _pixels, _pixels.Length);
            var all = Island.All;
            for (int k = 0; k < all.Count; k++)
            {
                var isl = all[k];
                if (isl == null || isl == player || !isl.isActiveAndEnabled) continue;
                RingPlot(g, isl.PlanarPosition, n, 2, MapIsland);
            }
            RingPlot(g, player.PlanarPosition, n, 4, MapPlayer);
            _mapTex.SetPixels32(_pixels);
            _mapTex.Apply(false);
        }

        void RingPlot(RingGeometry g, Vector2 planar, int n, int radius, Color32 col)
        {
            float across = g.halfWidth > 0f ? Mathf.Clamp((planar.x - g.centerX) / g.halfWidth, -1f, 1f) : 0f;
            float r = (RingInner + RingOuter) * 0.5f + across * (RingOuter - RingInner) * 0.5f;
            // Along the ring runs clockwise from the top, so driving forward reads as moving round the dial.
            float a = Mathf.PI * 0.5f - planar.y / g.circumference * 2f * Mathf.PI;
            float c = (n - 1) * 0.5f;
            int cx = Mathf.RoundToInt(c + Mathf.Cos(a) * r * n), cy = Mathf.RoundToInt(c + Mathf.Sin(a) * r * n);
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    _pixels[y * n + x] = col;
                }
        }

        static float Mod(float a, float m) => ((a % m) + m) % m;

        static readonly Color32 MapStorm = new Color32(176, 180, 200, 255);

        void Blend(Vector2 world, float w, int n, int radius, Color32 c, float amount)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(Mod(world.x, w) / w * n), 0, n - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(Mod(world.y, w) / w * n), 0, n - 1);
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    float d2 = (dx * dx + dy * dy) / (float)(radius * radius);
                    if (d2 > 1f) continue;
                    int x = (cx + dx + n) % n, y = (cy + dy + n) % n;
                    _pixels[y * n + x] = Color32.Lerp(_pixels[y * n + x], c, amount * (1f - d2 * d2));
                }
        }

        void Plot(Vector2 world, float w, int n, int radius, Color32 c)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(Mod(world.x, w) / w * n), 0, n - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(Mod(world.y, w) / w * n), 0, n - 1);
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = (cx + dx + n) % n, y = (cy + dy + n) % n;
                    _pixels[y * n + x] = c;
                }
        }

        // ---------------------------------------------------------------- layout

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            // Order 1, not 0: world renderers share order 0 and the fish (queue Transparent+5) would draw over the HUD.
            // The raycaster is for the tap on the cozy panel; the thumbstick does not start on a raycast graphic.
            _canvas = UiStyle.Canvas(transform, CanvasName, 1, true, out _root);
            var root = _root;

            _top = UiStyle.Panel(root, "TopPanel", new Vector2(PanelWidth, CozyHeight)).TopLeft(new Vector2(UiStyle.Margin, -UiStyle.Margin), new Vector2(PanelWidth, CozyHeight));
            var body = _top.Find("Body");
            _topBody = body != null ? body.GetComponent<Image>() : null;
            _topButton = _top.gameObject.AddComponent<Button>();
            _topButton.targetGraphic = _topBody;
            _topButton.transition = Selectable.Transition.None;
            _topButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _topButton.onClick.AddListener(ToggleExplain);
            var press = _top.gameObject.AddComponent<UiPressFeedback>();
            press.pressedScale = 0.98f;
            press.stretch = 1.01f;

            BuildCozy(_top);
            BuildAdventure(_top);
            _buoyBar = _cozyBuoyBar;
            BuildExplain(root);

            var mapFrame = _mapFrame = UiStyle.SquirclePanel(root, "MapFrame", MapSize).Place(Vector2.zero, Vector2.zero, new Vector2(UiStyle.Margin, UiStyle.Margin), new Vector2(MapSize, MapSize));
            // The frame only shows the map; it must not swallow the thumbstick in the corner.
            var frameBody = mapFrame.Find("Body");
            if (frameBody != null) frameBody.GetComponent<Image>().raycastTarget = false;
            _map = UiStyle.Picture(mapFrame, "Map", Vector2.zero, true, out var mapRect);
            mapRect.Stretch(18f, 18f, 18f, 18f);
            _mapTex = new Texture2D(mapPixels, mapPixels, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat };
            _pixels = new Color32[mapPixels * mapPixels];
            _map.texture = _mapTex;

            _hintRect = UiStyle.Chip(root, "HintPanel", "", new Vector2(HintWide, 64f), out _hintText);
            _hintText.lineSpacing = UiStyle.Lines(1.1f);
            _hintLayout = -1;
            PlaceHint();
            UiStyle.OnResize(_root, PlaceHint);
        }

        static RectTransform Row(RectTransform parent, string name, float top, float height) =>
            UiStyle.Rect(parent, name).TopLeft(new Vector2(Pad, -top), new Vector2(Inner, height));

        static Text RowText(RectTransform row, string text, int size, Color color, TextAnchor align, bool bold, float x, float width)
        {
            var t = UiStyle.Label(row, text, size, color, align, bold);
            var anchor = align == TextAnchor.MiddleRight ? new Vector2(1f, 0.5f) : align == TextAnchor.MiddleCenter ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
            t.rectTransform.anchorMin = new Vector2(anchor.x, 0f);
            t.rectTransform.anchorMax = new Vector2(anchor.x, 1f);
            t.rectTransform.pivot = anchor;
            t.rectTransform.anchoredPosition = new Vector2(x, 0f);
            t.rectTransform.sizeDelta = new Vector2(width, 0f);
            UiStyle.FitWidth(t);
            return t;
        }

        // A caption on the left, the bar in the middle, a value on the right.
        static UiBar LabelledBar(RectTransform row, string name, string caption, Color fill, float barHeight, float valueColumn, out Text label, out Text value)
        {
            label = RowText(row, caption, UiStyle.Caption, UiStyle.CreamSoft, TextAnchor.MiddleLeft, true, 0f, LabelColumn - 8f);
            float width = Inner - LabelColumn - valueColumn;
            var bar = UiStyle.Bar(row, name, new Vector2(width, barHeight), fill);
            bar.Root.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(LabelColumn, 0f), new Vector2(width, barHeight));
            value = RowText(row, "", UiStyle.Caption, UiStyle.CreamSoft, TextAnchor.MiddleRight, false, 0f, valueColumn - 16f);
            return bar;
        }

        void BuildCozy(RectTransform top)
        {
            _cozy = UiStyle.Rect(top, "Cozy").Stretch();
            var head = Row(_cozy, "Head", 16f, 58f);
            _areaText = RowText(head, "", UiStyle.Subheading, UiStyle.Cream, TextAnchor.MiddleLeft, true, 0f, 400f);
            _cozyStatusText = RowText(head, "", UiStyle.Caption, UiStyle.Sand, TextAnchor.MiddleRight, true, -52f, 280f);
            _seasonPill = UiStyle.Pill(head, "Season", new Vector2(140f, 40f), UiStyle.Ghost);
            _seasonText = UiStyle.Label(_seasonPill.transform, "", 26, UiStyle.Mint, TextAnchor.MiddleCenter, true);
            _seasonText.rectTransform.Stretch(0f, 0f, 0f, 2f);
            _seasonText.raycastTarget = false;
            _seasonPill.gameObject.SetActive(false);
            // The small "?" says the panel explains itself on a tap.
            var help = UiStyle.Dot(head, "Help", 40f, UiStyle.Ghost);
            help.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            var q = UiStyle.Label(help.transform, "?", UiStyle.Caption, UiStyle.CreamSoft, TextAnchor.MiddleCenter, true);
            q.rectTransform.Stretch(0f, 0f, 0f, 2f);

            _worldBar = LabelledBar(Row(_cozy, "World", 86f, 32f), "WorldBar", ModeTexts.WorldLabel, UiStyle.Mint, 24f, ValueColumn, out _, out _islandsText);
            _islandsText.color = UiStyle.Muted;
            _cozyBuoyBar = LabelledBar(Row(_cozy, "Buoyancy", 130f, 36f), "BuoyBar", ModeTexts.BuoyancyLabel, UiStyle.Sky, 30f, ValueColumn, out _, out _formText);
            // The buoyancy bar is tinted and glinted every frame: its own canvas, so the panel is not rebuilt with it.
            UiStyle.SubCanvas(_cozyBuoyBar.Root);
            var mile = Row(_cozy, "Milestone", 176f, 38f);
            _milestoneText = RowText(mile, "", UiStyle.Caption, UiStyle.Sand, TextAnchor.MiddleLeft, false, 0f, Inner);
        }

        void BuildAdventure(RectTransform top)
        {
            _adventure = UiStyle.Rect(top, "Adventure").Stretch();
            // Row 1: the distance (the score), the call of the moment, the record.
            var head = Row(_adventure, "Head", 12f, 76f);
            _timerText = RowText(head, "", 60, UiStyle.Cream, TextAnchor.MiddleLeft, true, 0f, 260f);
            _boostText = RowText(head, "", 40, UiStyle.Mint, TextAnchor.MiddleCenter, true, 15f, 230f);
            _bestText = RowText(head, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.MiddleRight, true, 0f, 220f);
            _boostText.gameObject.SetActive(false);
            // Row 2: what keeps you afloat, and the level.
            var row = Row(_adventure, "Buoyancy", 98f, 38f);
            _advBuoyBar = LabelledBar(row, "BuoyBar", ModeTexts.BuoyancyLabel, UiStyle.Sky, 32f, LevelColumn, out _advBuoyLabel, out _levelText);
            _levelText.color = UiStyle.Sand;
            _levelText.fontStyle = UiStyle.Weight(true);
            _advTrack = _advBuoyBar.Root.GetComponent<Image>();
            // Row 3: the momentum ("Schwung") that makes the island faster, as big as the buoyancy row above it.
            var mrow = Row(_adventure, "Momentum", 144f, 38f);
            _momentumBar = LabelledBar(mrow, "MomentumBar", ModeTexts.MomentumLabel, UiStyle.Sand, 32f, LevelColumn, out _momentumLabel, out _);
            _momentumLabel.color = UiStyle.Muted;
            BuildStartLine(_adventure);
            // Everything in the strip moves every frame (distance, calls, pulsing bars): its own canvas keeps those
            // rebuilds away from the rest of the HUD.
            UiStyle.SubCanvas(_adventure);
            _adventure.gameObject.SetActive(false);
        }

        void BuildStartLine(RectTransform adventure)
        {
            var panel = UiStyle.Panel(adventure, "StartLine", new Vector2(PanelWidth, StartLineHeight), true, false)
                .TopLeft(new Vector2(0f, -(AdventureHeight + UiStyle.GapSmall)), new Vector2(PanelWidth, StartLineHeight));
            // Broken after the dash: a free wrap split "wie | weit kommst du?".
            var text = UiStyle.Label(panel, ModeTexts.AdventureStartLine.Replace(" – ", " –\n"), UiStyle.Body, UiStyle.Cream, TextAnchor.MiddleCenter, true);
            text.lineSpacing = UiStyle.Lines(1.08f);
            text.rectTransform.Stretch(Pad, 8f, Pad, 8f);
            _startLine = panel.gameObject.AddComponent<CanvasGroup>();
            _startLine.interactable = false;
            _startLine.blocksRaycasts = false;
            _startLine.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        void BuildExplain(RectTransform root)
        {
            const float width = PanelWidth, height = 318f;
            _explain = UiStyle.Panel(root, "HudExplain", new Vector2(width, height), true)
                .TopLeft(new Vector2(UiStyle.Margin, -(UiStyle.Margin + CozyHeight + UiStyle.GapSmall)), new Vector2(width, height));
            var body = _explain.Find("Body");
            var button = _explain.gameObject.AddComponent<Button>();
            button.targetGraphic = body != null ? body.GetComponent<Image>() : null;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(HideExplain);
            ExplainLine(_explain, 26f, UiStyle.Sand, ModeTexts.ExplainSinking);
            ExplainLine(_explain, 112f, UiStyle.Mint, ModeTexts.ExplainWorld);
            ExplainLine(_explain, 166f, UiStyle.Sky, ModeTexts.ExplainBuoyancy);
            var close = UiStyle.Label(_explain, ModeTexts.ExplainClose, 26, UiStyle.Faint, TextAnchor.MiddleRight);
            close.rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Pad, 16f), new Vector2(Inner, 36f));
            _explain.gameObject.SetActive(false);
        }

        static void ExplainLine(RectTransform card, float top, Color dot, string text)
        {
            UiStyle.Dot(card, "Dot", 20f, dot).rectTransform.TopLeft(new Vector2(Pad, -top - 12f), new Vector2(20f, 20f));
            var t = UiStyle.Label(card, text, UiStyle.Caption, UiStyle.Cream, TextAnchor.UpperLeft);
            t.lineSpacing = UiStyle.Lines(1.08f);
            t.rectTransform.TopLeft(new Vector2(Pad + 36f, -top), new Vector2(Inner - 36f, 80f));
        }

        float MapEdge => _shownMode == GameMode.Adventure ? MapSizeAdventure : MapSize;

        // Beside the map a wide one-line chip does not fit a portrait screen: there the hint wraps onto two lines.
        // The race keeps a smaller two-line chip beside its smaller map.
        void PlaceHint()
        {
            if (_hintRect == null || _root == null) return;
            if (_shownMode == GameMode.Adventure) { ApplyHintLayout(2); return; }
            ApplyHintLayout(_root.rect.width < 2f * UiStyle.Margin + MapEdge + UiStyle.Gap + HintWide ? 1 : 0);
        }

        void ApplyHintLayout(int layout)
        {
            if (layout == _hintLayout) return;
            _hintLayout = layout;
            var size = layout == 2 ? new Vector2(HintAdventure, 96f) : layout == 1 ? new Vector2(HintNarrow, 104f) : new Vector2(HintWide, 64f);
            _hintRect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-UiStyle.Margin, UiStyle.Margin), size);
            FillHint();
        }

        void FillHint()
        {
            int state = Mathf.Max(0, _lastHintState);
            bool touch = (state & 1) != 0, following = (state & 2) != 0;
            var mode = (state & 4) != 0 ? GameMode.Adventure : GameMode.Cozy;
            string br = _hintLayout >= 1 ? "\n" : "  ·  ";
            string zoom = touch ? "Zwei Finger: Zoom" : "Q/E Zoom  ·  Esc Pause";
            _hintText.text = following
                ? (touch ? "Ziehen: drehen" + br + "Zwei Finger: Zoom" : "A/D drehen  ·  W/S neigen" + br + "Q/E Zoom  ·  R zurück")
                : ModeTexts.SteerHint(mode, touch, (state & 8) != 0, (state & 32) != 0) + br + zoom;
        }
    }
}
