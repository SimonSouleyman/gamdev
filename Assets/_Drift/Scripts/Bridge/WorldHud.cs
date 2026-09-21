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

        const float MapSize = 340f, HintWide = 760f, HintNarrow = 610f;

        Canvas _canvas;
        RectTransform _root, _hintRect;
        int _hintNarrow = -1;
        WatchTools _watch;
        Text _areaText, _progressText, _sinkText, _formText, _hintText;
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
            _timer = refresh;
        }

        void OnDisable()
        {
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

            float buoy = player.Buoyancy;
            _buoyBar.Set(buoy);
            Color buoyColor = Color.Lerp(UiStyle.Coral, UiStyle.Sky, Mathf.Clamp01((buoy - 0.25f) / 0.6f));
            if (buoy < 0.35f) buoyColor = Color.Lerp(buoyColor, UiStyle.Cream, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
            _buoyBar.Fill.color = buoyColor;
            _buoyBar.Animate(Time.unscaledTime);

            _timer += Time.unscaledDeltaTime;
            if (_timer < refresh) return;
            _timer = 0f;

            float remaining = 0f;
            int left = 0;
            if (streamer != null) streamer.Progress(out remaining, out left);
            float area = player.LandArea;
            float total = area + remaining;
            float pct = total > 0f ? area / total : 1f;

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
                _progressText.text = left > 0 ? pctI + " % der Welt  ·  " + left + " Inseln übrig" : "Die Welt ist vereint!";
            }
            int sinkState = buoy < 0.55f ? 1 : 0;
            int form = Mathf.RoundToInt(player.Compactness * 20f) * 5;
            if (sinkState != _lastSinkState || form != _lastForm)
            {
                _lastSinkState = sinkState;
                _lastForm = form;
                _sinkText.text = sinkState == 1 ? "Sinkt – sammle Inseln!" : "Auftrieb";
                _sinkText.color = sinkState == 1 ? UiStyle.Coral : UiStyle.CreamSoft;
                _formText.text = form < 50 ? "Form " + form + " %  ·  länglich" : "Form " + form + " %  ·  rund";
                _formText.color = form < 50 ? UiStyle.Coral : UiStyle.CreamSoft;
            }
            if (_watch == null && _lookupTimer <= 0f) _watch = FindAnyObjectByType<WatchTools>();
            bool following = _watch != null && _watch.Following;
            int hintState = (InputMode.TouchPreferred ? 1 : 0) + (following ? 2 : 0);
            if (hintState != _lastHintState)
            {
                _lastHintState = hintState;
                FillHint();
            }
            DrawMap();
        }

        void DrawMap()
        {
            if (_mapTex == null || streamer == null || streamer.WorldSize <= 0f) return;
            int n = mapPixels;
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = MapBg;

            float w = streamer.WorldSize;
            var slots = streamer.WorldSlots();
            for (int k = 0; k < slots.Count; k++)
            {
                var s = slots[k];
                if (s.consumed) continue;
                int r = Mathf.Clamp(Mathf.RoundToInt(s.radius / w * n * 1.6f), 1, 4);
                Plot(s.pos, w, n, r, MapIsland);
            }
            int pr = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(player.LandArea) / w * n * 1.3f), 2, 8);
            Plot(player.PlanarPosition, w, n, pr, MapPlayer);

            _mapTex.SetPixels32(_pixels);
            _mapTex.Apply(false);
        }

        static float Mod(float a, float m) => ((a % m) + m) % m;

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
            const float inner = 740f, pad = 40f, height = 300f;
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
            foreach (var t in new[] { _areaText, _progressText, _sinkText, _formText }) UiStyle.FitWidth(t);

            var mapFrame = UiStyle.SquirclePanel(root, "MapFrame", MapSize).Place(Vector2.zero, Vector2.zero, new Vector2(UiStyle.Margin, UiStyle.Margin), new Vector2(MapSize, MapSize));
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
            string br = _hintNarrow == 1 ? "\n" : "  ·  ";
            _hintText.text = state >= 2
                ? (state == 3 ? "Ziehen: drehen" + br + "Zwei Finger: Zoom" : "A/D drehen  ·  W/S neigen" + br + "Q/E Zoom  ·  R zurück")
                : state == 1
                ? "Stick links: lenken & Tempo" + br + "Zwei Finger: Zoom"
                : "W/S Tempo  ·  A/D lenken" + br + "Q/E Zoom  ·  Esc Pause";
        }
    }
}
