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

        public Island player;
        public WorldStreamer streamer;
        public GameSession session;
        public IslandChaseCamera chaseCamera;
        public int mapPixels = 128;
        public float refresh = 0.3f;
        // Edit mode only: which mode's HUD the Game view shows (Auto = GameModes.Current).
        public EditorMode editorMode = EditorMode.Auto;

        public enum EditorMode { Auto, Cozy, Adventure }

        const float MapSize = 340f, HintWide = 760f, HintNarrow = 610f;

        Canvas _canvas;
        RectTransform _root, _hintRect;
        int _hintNarrow = -1;
        WatchTools _watch;
        Text _areaText, _progressText, _sinkText, _formText, _hintText, _milestoneText;
        string _lastMilestone = "";
        Text _timerText, _bestText, _boostText;
        RectTransform _mapFrame;
        GameMode _shownMode = (GameMode)(-1);
        int _lastTimer = int.MinValue, _lastBest = int.MinValue, _lastBoost = int.MinValue;
        UiBar _worldBar, _buoyBar;
        RawImage _map;
        Texture2D _mapTex;
        Color32[] _pixels;
        float _timer;
        float _lookupTimer;
        int _lastArea = int.MinValue, _lastPct = int.MinValue, _lastLeft = int.MinValue;
        int _lastSinkState = -1, _lastHintState = -1, _lastForm = -1;

        static readonly Color32 MapBg = new Color32(18, 52, 72, 255);
        static readonly Color32 MapIsland = new Color32(140, 222, 176, 255);
        static readonly Color32 MapPlayer = new Color32(248, 217, 158, 255);

        // Targets of the tutorial's highlight ring.
        public RectTransform BuoyancyRect => _buoyBar != null ? _buoyBar.Root : null;
        public RectTransform FormRect => _formText != null ? _formText.rectTransform : null;

        void OnEnable()
        {
            Build();
            _lastArea = _lastPct = _lastLeft = int.MinValue;
            _lastSinkState = _lastHintState = _lastForm = -1;
            _lastTimer = _lastBest = _lastBoost = int.MinValue;
            _shownMode = (GameMode)(-1);
            _timer = refresh;
            Island.Bumped -= OnBumped;
            Island.Bumped += OnBumped;
            RingWorld.Dodged -= OnDodged;
            RingWorld.Dodged += OnDodged;
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

        void OnDisable()
        {
            Island.Bumped -= OnBumped;
            RingWorld.Dodged -= OnDodged;
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
            if (_lookupTimer <= 0f && (streamer == null || session == null || chaseCamera == null))
            {
                _lookupTimer = 1f;
                if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
                if (session == null) session = FindAnyObjectByType<GameSession>();
                if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
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
                // Abenteuer: the danger meter, coral and pulsing when the next merge is urgent.
                buoyColor = Color.Lerp(UiStyle.Coral, UiStyle.Sky, Mathf.Clamp01((buoy - 0.25f) / 0.6f));
                if (buoy < 0.35f) buoyColor = Color.Lerp(buoyColor, UiStyle.Cream, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
            }
            else buoyColor = Color.Lerp(UiStyle.Sand, UiStyle.Sky, Mathf.Clamp01((buoy - 0.3f) / 0.5f));
            _buoyBar.Fill.color = buoyColor;
            _buoyBar.Animate(Time.unscaledTime);
            if (adventure) UpdateAdventure();

            _timer += Time.unscaledDeltaTime;
            if (_timer < refresh) return;
            _timer = 0f;

            if (!adventure) UpdateCozyProgress();
            int sinkState = buoy < ModeTexts.HeavyBuoyancy ? 1 : 0;
            // The island keeps its size in Abenteuer, so the form line shows the difficulty level instead.
            var ringWorld = adventure ? RingWorld.Active : null;
            int form = adventure ? (ringWorld != null ? ringWorld.Level : 1) : Mathf.RoundToInt(player.Compactness * 20f) * 5;
            if (sinkState != _lastSinkState || form != _lastForm)
            {
                _lastSinkState = sinkState;
                _lastForm = form;
                _sinkText.text = ModeTexts.SinkLabel(mode, buoy);
                _sinkText.color = sinkState == 1 ? (adventure ? UiStyle.Coral : UiStyle.Sand) : UiStyle.CreamSoft;
                if (adventure)
                {
                    _formText.text = ModeTexts.LevelLabel(form);
                    _formText.color = UiStyle.Sand;
                }
                else
                {
                    _formText.text = form < 50 ? "Form " + form + " %  ·  länglich" : "Form " + form + " %  ·  rund";
                    _formText.color = form < 50 ? UiStyle.Sand : UiStyle.CreamSoft;
                }
            }
            if (_watch == null && _lookupTimer <= 0f) _watch = FindAnyObjectByType<WatchTools>();
            bool following = _watch != null && _watch.Following;
            int hintState = (InputMode.TouchPreferred ? 1 : 0) + (following ? 2 : 0)
                + (adventure ? 4 : 0) + (Island.DirectionSteering ? 8 : 0);
            if (hintState != _lastHintState)
            {
                _lastHintState = hintState;
                FillHint();
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

        // Gemuetlich: land mass and "x von N Inseln" towards the Pangaea, plus the minimap. Abenteuer: the run timer,
        // the best time and the boost, and the minimap draws the ring band instead (hidden while no ring is applied).
        void ApplyMode(GameMode mode)
        {
            _shownMode = mode;
            bool adventure = mode == GameMode.Adventure;
            SetActive(_areaText, !adventure);
            SetActive(_progressText, !adventure);
            SetActive(_milestoneText, !adventure);
            if (_worldBar != null && _worldBar.Root.gameObject.activeSelf == adventure) _worldBar.Root.gameObject.SetActive(!adventure);
            SetActive(_timerText, adventure);
            SetActive(_bestText, adventure);
            if (!adventure) SetActive(_boostText, false);
            if (_mapFrame != null && !_mapFrame.gameObject.activeSelf) _mapFrame.gameObject.SetActive(true);
            _ringBgBuilt = false;
            _lastArea = _lastPct = _lastLeft = _lastTimer = _lastBest = _lastBoost = int.MinValue;
            _lastSinkState = _lastForm = -1;
            _lastMilestone = "";
            _timer = refresh;
        }

        static void SetActive(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        void UpdateCozyProgress()
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
            int pctI = Mathf.RoundToInt(pct * 100f);
            if (pctI != _lastPct || left != _lastLeft)
            {
                _lastPct = pctI;
                _lastLeft = left;
                _progressText.text = ModeTexts.CozyProgress(total - left, total, pctI);
            }
            // The next milestone is the reason to look forward to the next merge (Leuchtturm, Hafen, ...).
            string next = session != null ? Drift.SaveSystem.Milestones.NextText(session.Stats.islandsAbsorbed) : "";
            if (next != _lastMilestone)
            {
                _lastMilestone = next;
                if (_milestoneText != null) _milestoneText.text = next;
            }
        }

        // Every frame, but the strings only change once a second (timer, best time) or with the boost.
        void UpdateAdventure()
        {
            bool live = Application.isPlaying && session != null;
            float t = live ? session.Stats.timeSurvived : 222f;
            float best = live ? BestTimes.Get(GameMode.Adventure) : 262f;
            int ti = Mathf.FloorToInt(t);
            if (ti != _lastTimer)
            {
                _lastTimer = ti;
                _timerText.text = BestTimes.Format(t);
            }
            // Past the old best the line turns into a cheer.
            int bi = best > 0f && t > best ? -2 : Mathf.FloorToInt(best);
            if (bi != _lastBest)
            {
                _lastBest = bi;
                _bestText.text = bi == -2 ? "Neuer Rekord!" : best > 0f ? "Bestzeit " + BestTimes.Format(best) : "Erster Versuch";
                _bestText.color = bi == -2 ? UiStyle.Mint : UiStyle.Muted;
            }
            // One line for the calls of the race, in order: a hit or a dodge just now, the flotsam boost, surfing.
            if (_callLeft > 0f) _callLeft -= Time.unscaledDeltaTime;
            bool boosting = live ? player.Boosting : editorMode == EditorMode.Adventure;
            float factor = live ? player.BoostFactor : 1.25f;
            bool surfing = live && player.SurfStrength > 0.35f;
            // Near the rim of the band: a short note that the world ends there, not an alarm - nothing can happen.
            bool atEdge = live && player.EdgeWarning > 0.6f;
            int state = atEdge ? 4 : _callLeft > 0f ? 3 : boosting ? 1 : surfing ? 2 : 0;
            int boostKey = state == 0 ? 0 : state == 4 ? 4000 : state == 3 ? 3000 + _callText.Length : state * 1000 + Mathf.RoundToInt(factor * 10f);
            if (boostKey != _lastBoost)
            {
                _lastBoost = boostKey;
                SetActive(_boostText, state != 0);
                if (state == 4) _boostText.text = ModeTexts.EdgeLabel;
                else if (state == 3) _boostText.text = _callText;
                else if (state == 1) _boostText.text = ModeTexts.BoostLabel(factor);
                else if (state == 2) _boostText.text = ModeTexts.SurfLabel;
            }
            if (state == 4) _boostText.color = UiStyle.WithAlpha(UiStyle.Sand, 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f));
            else if (state == 3) _boostText.color = UiStyle.WithAlpha(_callColor, 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 12f));
            else if (state == 1) _boostText.color = UiStyle.WithAlpha(UiStyle.Mint, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f));
            else if (state == 2) _boostText.color = UiStyle.WithAlpha(UiStyle.Sky, 0.8f);
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
            _canvas = UiStyle.Canvas(transform, CanvasName, 1, false, out _root);
            var root = _root;

            // Left-anchored and 820 wide so the round buttons of SessionScreens / WatchTools (top right, 120 wide) stay clear in portrait.
            const float inner = 740f, pad = 40f, height = 342f;
            var top = UiStyle.Panel(root, "TopPanel", new Vector2(inner + 2f * pad, height)).TopLeft(new Vector2(UiStyle.Margin, -UiStyle.Margin), new Vector2(inner + 2f * pad, height));
            _areaText = UiStyle.Label(top, "", 50, UiStyle.Cream, TextAnchor.UpperLeft, true);
            _areaText.rectTransform.TopLeft(new Vector2(pad, -22f), new Vector2(inner, 62f));
            _worldBar = UiStyle.Bar(top, "WorldBar", new Vector2(inner, 26f), UiStyle.Mint);
            _worldBar.Root.TopLeft(new Vector2(pad, -94f), new Vector2(inner, 26f));
            _progressText = UiStyle.Label(top, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperLeft);
            _progressText.rectTransform.TopLeft(new Vector2(pad, -128f), new Vector2(inner, 38f));
            _buoyBar = UiStyle.Bar(top, "BuoyBar", new Vector2(inner, 34f), UiStyle.Sky);
            _buoyBar.Root.TopLeft(new Vector2(pad, -184f), new Vector2(inner, 34f));
            _sinkText = UiStyle.Label(top, "", UiStyle.Caption, UiStyle.CreamSoft, TextAnchor.UpperLeft);
            _sinkText.rectTransform.TopLeft(new Vector2(pad, -232f), new Vector2(400f, 38f));
            _formText = UiStyle.Label(top, "", UiStyle.Caption, UiStyle.CreamSoft, TextAnchor.UpperRight);
            _formText.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-pad, -232f), new Vector2(330f, 38f));
            _milestoneText = UiStyle.Label(top, "", UiStyle.Caption, UiStyle.Sand, TextAnchor.UpperLeft);
            _milestoneText.rectTransform.TopLeft(new Vector2(pad, -276f), new Vector2(inner, 38f));
            _timerText = UiStyle.Label(top, "", UiStyle.Title, UiStyle.Cream, TextAnchor.UpperLeft, true);
            _timerText.rectTransform.TopLeft(new Vector2(pad, -18f), new Vector2(400f, 130f));
            _bestText = UiStyle.Label(top, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperRight, true);
            _bestText.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-pad, -30f), new Vector2(330f, 40f));
            _boostText = UiStyle.Label(top, "", UiStyle.Subheading, UiStyle.Mint, TextAnchor.UpperRight, true);
            _boostText.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-pad, -84f), new Vector2(330f, 56f));
            foreach (var t in new[] { _areaText, _progressText, _sinkText, _formText, _bestText, _boostText }) UiStyle.FitWidth(t);
            _timerText.gameObject.SetActive(false);
            _bestText.gameObject.SetActive(false);
            _boostText.gameObject.SetActive(false);

            var mapFrame = _mapFrame = UiStyle.SquirclePanel(root, "MapFrame", MapSize).Place(Vector2.zero, Vector2.zero, new Vector2(UiStyle.Margin, UiStyle.Margin), new Vector2(MapSize, MapSize));
            _map = UiStyle.Picture(mapFrame, "Map", Vector2.zero, true, out var mapRect);
            mapRect.Stretch(18f, 18f, 18f, 18f);
            _mapTex = new Texture2D(mapPixels, mapPixels, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat };
            _pixels = new Color32[mapPixels * mapPixels];
            _map.texture = _mapTex;

            _hintRect = UiStyle.Chip(root, "HintPanel", "", new Vector2(HintWide, 64f), out _hintText);
            _hintText.lineSpacing = UiStyle.Lines(1.1f);
            _hintNarrow = -1;
            PlaceHint();
            UiStyle.OnResize(_root, PlaceHint);
        }

        // Beside the map a wide one-line chip does not fit a portrait screen: there the hint wraps onto two lines.
        void PlaceHint()
        {
            if (_hintRect == null || _root == null) return;
            ApplyHintLayout(_root.rect.width < 2f * UiStyle.Margin + MapSize + UiStyle.Gap + HintWide);
        }

        void ApplyHintLayout(bool narrow)
        {
            if ((narrow ? 1 : 0) == _hintNarrow) return;
            _hintNarrow = narrow ? 1 : 0;
            _hintRect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-UiStyle.Margin, UiStyle.Margin), narrow ? new Vector2(HintNarrow, 104f) : new Vector2(HintWide, 64f));
            FillHint();
        }

        void FillHint()
        {
            int state = Mathf.Max(0, _lastHintState);
            bool touch = (state & 1) != 0, following = (state & 2) != 0;
            var mode = (state & 4) != 0 ? GameMode.Adventure : GameMode.Cozy;
            string br = _hintNarrow == 1 ? "\n" : "  ·  ";
            string zoom = touch ? "Zwei Finger: Zoom" : "Q/E Zoom  ·  Esc Pause";
            _hintText.text = following
                ? (touch ? "Ziehen: drehen" + br + "Zwei Finger: Zoom" : "A/D drehen  ·  W/S neigen" + br + "Q/E Zoom  ·  R zurück")
                : ModeTexts.SteerHint(mode, touch, (state & 8) != 0) + br + zoom;
        }
    }
}
