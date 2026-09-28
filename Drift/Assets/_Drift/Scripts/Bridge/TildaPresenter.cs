using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Tilda standing big beside a menu, as if she presented it. One presenter per canvas; the screen that is up calls
    // Present() every frame with its scrim and panel size, and the presenter moves into that scrim *behind* the panel.
    // Wide canvases: she stands left of the panel with her feet near the bottom edge. Narrow ones (portrait phones):
    // she peeks over the panel's top edge, smaller, clipped at that edge by a RectMask2D (the glass panel is
    // translucent, her body would shine through). She hops in on unscaled time and sits on a soft shadow. Her short
    // remarks (TildaVoice.LineStarted) appear in a speech bubble beside her head, its tail at her mouth.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class TildaPresenter : MonoBehaviour
    {
        public enum Placement { Hidden, Side, Peek }

        public struct Layout
        {
            public Placement placement;
            public float size;
            // Centre of her square picture, relative to the centre of the canvas.
            public Vector2 center;
        }

        // Of the square full-body portrait: the front rim of her base is this far above the lower edge, the middle of
        // the base (where the shadow lies) this far, her body is this wide around the centre, and the panel edge
        // cuts a peeking Tilda just below the mouth, her crater rim being at PeekBodyTop.
        public const float FeetFraction = 0.05f, GroundFraction = 0.15f, BodyHalfWidth = 0.37f, PeekCut = 0.3f, PeekBodyTop = 0.65f;
        public const float ShadowWidth = 0.92f, ShadowHeight = 0.36f, ShadowAlpha = 0.85f;
        public const float PeekOffset = 190f;
        public const float MinSideStrip = 440f, MinPeekStrip = 90f, MinPeekSize = 250f, MaxPeekSize = 540f;
        // Her feet stand this much of her size above the bottom margin, so the shadow in front of them has room.
        public const float FeetLift = 0.05f;
        const float EnterSeconds = 0.85f;
        // The remark bubble: beside her head on the side away from the panel, or left of a peeking Tilda.
        public const float RemarkWidth = 600f, MinRemarkWidth = 420f;

        public struct RemarkLayout
        {
            public float width;
            // Lower right corner of the bubble and her mouth, relative to the centre of the canvas.
            public Vector2 corner, mouth;
        }

        // Where a remark goes for a given presenter layout. Side: up and to the left of her head, clear of her face,
        // inside the left margin. Peek: left of her, resting on the panel's top edge as far as the screen allows.
        public static RemarkLayout Remark(Layout l, Vector2 canvas, Vector2 panel, float bubbleHeight)
        {
            var r = new RemarkLayout();
            Vector2 m = TildaPortrait.MouthIn(true);
            r.mouth = l.center + new Vector2((m.x - 0.5f) * l.size, (m.y - 0.5f) * l.size);
            float leftEdge = -canvas.x * 0.5f + UiStyle.Margin * 0.5f;
            if (l.placement == Placement.Side)
            {
                float right = l.center.x - 0.2f * l.size;
                r.width = Mathf.Clamp(right - leftEdge, MinRemarkWidth, RemarkWidth);
                r.corner = new Vector2(Mathf.Max(right, leftEdge + r.width), l.center.y + 0.02f * l.size);
                return r;
            }
            float peekRight = l.center.x - 0.27f * l.size;
            r.width = Mathf.Clamp(peekRight - leftEdge, MinRemarkWidth, RemarkWidth);
            float bottom = panel.y * 0.5f + 14f;
            float top = canvas.y * 0.5f - 14f - TildaBubble.NameOverhang;
            if (bottom + bubbleHeight > top) bottom = top - bubbleHeight;
            r.corner = new Vector2(Mathf.Max(peekRight, leftEdge + r.width), bottom);
            return r;
        }

        // Edit Mode only: lay out as if the canvas had this size. `capture_game_view --width 540 --height 960` renders
        // a true portrait canvas, but no Update runs under it, so a portrait preview sets (1080, 1920) here first.
        public static Vector2 EditorCanvas;

        public static Vector2 CanvasSize(RectTransform root) =>
            !Application.isPlaying && EditorCanvas.x > 0f && EditorCanvas.y > 0f ? EditorCanvas : root.rect.size;

        // fraction: her picture's share of the canvas height when she stands beside the panel. The HUD minimap
        // (340 + margin, bottom left) stays clear of her body.
        public static Layout Compute(Vector2 canvas, Vector2 panel, float fraction, bool allowPeek = true)
        {
            var l = new Layout();
            float strip = (canvas.x - panel.x) * 0.5f - UiStyle.Margin;
            if (strip >= MinSideStrip)
            {
                float reserved = UiStyle.Margin + 340f + UiStyle.Gap;
                float room = (canvas.x - panel.x) * 0.5f - reserved - UiStyle.Gap;
                float size = Mathf.Min(fraction * canvas.y, Mathf.Max(MinSideStrip, room) / (2f * BodyHalfWidth));
                l.placement = Placement.Side;
                l.size = size;
                l.center = new Vector2(-panel.x * 0.5f - UiStyle.Gap - BodyHalfWidth * size,
                    -canvas.y * 0.5f + UiStyle.Margin + (0.5f - FeetFraction + FeetLift) * size);
                return l;
            }
            float above = (canvas.y - panel.y) * 0.5f;
            if (!allowPeek || above < MinPeekStrip) return l;
            l.placement = Placement.Peek;
            l.size = Mathf.Clamp((above - 30f) / (PeekBodyTop - PeekCut), MinPeekSize, MaxPeekSize);
            // Right of the middle, but over the straight part of the rounded panel edge.
            float x = Mathf.Min(PeekOffset, panel.x * 0.5f - UiStyle.PanelRadius - BodyHalfWidth * l.size);
            l.center = new Vector2(Mathf.Max(0f, x), panel.y * 0.5f + (0.5f - PeekCut) * l.size);
            return l;
        }

        RectTransform _rect, _shadow, _window, _figure;
        TildaBubble _bubble;
        string _lineKey;
        Vector2 _canvasSize, _panelSize;
        Image _shadowImage;
        TildaView _view;
        Transform _host;
        Canvas _canvas;
        Layout _layout;
        float _enter = 1f;

        public TildaView View => _view;
        public Placement Current => gameObject.activeSelf ? _layout.placement : Placement.Hidden;
        public Layout CurrentLayout => _layout;
        public bool Arrived => _enter >= 1f;

        public static TildaPresenter Create(RectTransform parent)
        {
            var rect = UiStyle.Rect(parent, "TildaPresenter");
            rect.Center(Vector2.zero, Vector2.zero);
            var p = rect.gameObject.AddComponent<TildaPresenter>();
            p._rect = rect;
            p._shadowImage = UiStyle.Shape(rect, "Shadow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Shadow, ShadowAlpha));
            p._shadow = p._shadowImage.rectTransform;
            p._window = UiStyle.Rect(rect, "Window");
            p._window.gameObject.AddComponent<RectMask2D>();
            var raw = TildaPortrait.CreateImage(p._window, new Vector2(600f, 600f));
            p._figure = raw.rectTransform;
            p._figure.Center(Vector2.zero, new Vector2(600f, 600f));
            p._view = raw.GetComponent<TildaView>();
            p._view.SetResolution(TildaPortrait.LargeSize);
            p._view.SetFullBody(true);
            p._bubble = TildaBubble.Create(parent, "TildaRemark", RemarkWidth, false);
            p._bubble.gameObject.SetActive(false);
            rect.gameObject.SetActive(false);
            return p;
        }

        // host: the full-screen scrim of the screen that is up (its panel is a child); canvas: the canvas root rect.
        // Returns where she is, so the screen can drop its own small Tilda when the big one stands beside it.
        public Placement Present(RectTransform host, Vector2 canvas, Vector2 panel, float fraction, bool allowPeek = true)
        {
            var layout = Compute(canvas, panel, fraction, allowPeek);
            if (layout.placement == Placement.Hidden || host == null)
            {
                Dismiss();
                return Placement.Hidden;
            }
            // Title <-> Anleitung only changes the scrim she belongs to: she stays where she stands, no second entrance.
            bool entering = !gameObject.activeSelf || layout.placement != _layout.placement;
            if (_host != host || transform.parent != host)
            {
                _host = host;
                transform.SetParent(host, false);
                _canvas = host.GetComponentInParent<Canvas>();
            }
            if (transform.GetSiblingIndex() != 0) transform.SetSiblingIndex(0);
            if (_bubble != null && _bubble.transform.parent != host) _bubble.transform.SetParent(host, false);
            _layout = layout;
            _canvasSize = canvas;
            _panelSize = panel;
            // 384 px are enough up to about 400 screen pixels; beside a menu on a desktop screen she is larger.
            float pixels = layout.size * (_canvas != null ? _canvas.rootCanvas.scaleFactor : 1f);
            _view.SetResolution(pixels > 400f ? TildaPortrait.LargeSize : TildaPortrait.Size);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (entering) _enter = Application.isPlaying ? 0f : 1f;
            Apply();
            return layout.placement;
        }

        public void Dismiss()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            _host = null;
        }

        void OnEnable()
        {
            TildaVoice.LineStarted += OnLineStarted;
            TildaVoice.LineEnded += OnLineEnded;
        }

        // Her remark leaves with her.
        void OnDisable()
        {
            TildaVoice.LineStarted -= OnLineStarted;
            TildaVoice.LineEnded -= OnLineEnded;
            if (_lineKey != null && Application.isPlaying) TildaVoice.Hush(_lineKey);
            HideBubble();
        }

        void OnLineStarted(TildaLine line)
        {
            if (line.shown || _bubble == null || !Application.isPlaying || Current == Placement.Hidden) return;
            line.shown = true;
            ShowRemark(line.key, line.text, line.mood);
        }

        void OnLineEnded(string key)
        {
            if (key == _lineKey) HideBubble();
        }

        void ShowRemark(string key, string rich, Drift.Audio.GrumbleMood mood)
        {
            _lineKey = key;
            if (!_bubble.gameObject.activeSelf) _bubble.gameObject.SetActive(true);
            _bubble.transform.SetAsLastSibling();
            PlaceBubble();
            if (Application.isPlaying) _bubble.Show(rich, mood, TildaBubble.RemarkCharsPerSecond, true, () => TildaVoice.Hush(key), TildaBubble.RemarkMaxChars);
            else _bubble.ShowStill(rich, TildaBubble.RemarkMaxChars);
            PlaceBubble();
        }

        // Edit Mode previews and verification: shows a remark without the queue (null hides it again).
        public void PreviewRemark(string rich)
        {
            if (_bubble == null) return;
            if (string.IsNullOrEmpty(rich) || Current == Placement.Hidden) HideBubble();
            else ShowRemark(null, rich, Drift.Audio.GrumbleMood.Warm);
        }

        public TildaBubble RemarkBubble => _bubble;

        void HideBubble()
        {
            _lineKey = null;
            if (_bubble != null && _bubble.gameObject.activeSelf) _bubble.gameObject.SetActive(false);
        }

        void PlaceBubble()
        {
            if (_bubble == null || !_bubble.gameObject.activeSelf || _layout.placement == Placement.Hidden) return;
            var r = Remark(_layout, _canvasSize, _panelSize, _bubble.Height);
            _bubble.SetWidth(r.width);
            r = Remark(_layout, _canvasSize, _panelSize, _bubble.Height);
            var size = new Vector2(r.width, _bubble.Height);
            if (_bubble.Rect.anchoredPosition == r.corner && _bubble.Rect.sizeDelta == size && _bubble.Rect.pivot == new Vector2(1f, 0f)) return;
            _bubble.Rect.Place(new Vector2(0.5f, 0.5f), new Vector2(1f, 0f), r.corner, size);
            Vector3 world = _bubble.Rect.parent.TransformPoint(new Vector3(r.mouth.x, r.mouth.y, 0f));
            _bubble.PointTailAt(world, (_layout.placement == Placement.Side ? 0.19f : 0.22f) * _layout.size);
        }

        void Update()
        {
            if (_view == null) return;
            if (_enter < 1f) _enter = Mathf.Min(1f, _enter + Time.unscaledDeltaTime / EnterSeconds);
            Apply();
        }

        void Apply()
        {
            if (_rect == null || _layout.placement == Placement.Hidden) return;
            float size = _layout.size;
            float t = Mathf.Clamp01(_enter);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector2 offset;
            float lift;
            if (_layout.placement == Placement.Side)
            {
                // Three hops in from beyond the left screen edge, each one lower than the last.
                lift = t < 1f ? Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (1f - t) * 0.16f * size : 0f;
                offset = new Vector2(-(1f - ease) * (size * 1.2f + 300f), lift);
            }
            else
            {
                float back = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f);
                lift = 0f;
                offset = new Vector2(0f, -(1f - back) * 0.45f * size);
            }

            // Beside the panel the window is simply huge; peeking, its lower edge is the panel's top edge.
            bool grounded = _layout.placement == Placement.Side;
            Vector2 windowCenter = grounded ? Vector2.zero : new Vector2(0f, (PeekCut + 0.1f) * size);
            _rect.anchoredPosition = _layout.center;
            _window.Center(windowCenter, grounded ? new Vector2(12000f, 12000f) : new Vector2(1.5f * size, 1.2f * size));
            _figure.anchoredPosition = offset - windowCenter;
            _figure.sizeDelta = new Vector2(size, size);
            PlaceBubble();

            if (_shadow.gameObject.activeSelf != grounded) _shadow.gameObject.SetActive(grounded);
            if (!grounded) return;
            // The shadow stays on the ground while she hops: smaller and fainter the higher she is.
            float hop = Mathf.Clamp01(TildaPortrait.HopHeight / 0.24f) * 0.06f * size + lift;
            float k = 1f - 0.4f * Mathf.Clamp01(hop / (0.16f * size));
            _shadow.sizeDelta = new Vector2(ShadowWidth * size * k, ShadowHeight * size * k);
            _shadow.anchoredPosition = new Vector2(offset.x, -(0.5f - GroundFraction) * size);
            _shadowImage.color = UiStyle.WithAlpha(UiStyle.Shadow, ShadowAlpha * k);
        }
    }
}
