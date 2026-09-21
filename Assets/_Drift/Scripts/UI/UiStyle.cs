using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.UI
{
    public sealed class UiBar
    {
        public readonly RectTransform Root;
        public readonly Image Fill;
        readonly RectTransform _fillRect;
        RectTransform _shine;
        CanvasRenderer _shineRenderer;
        float _value = -1f;

        public UiBar(RectTransform root, Image fill)
        {
            Root = root;
            Fill = fill;
            _fillRect = fill.rectTransform;
        }

        public float Value => _value;

        public void Set(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(value - _value) < 0.002f) return;
            _value = value;
            // A sliced pill narrower than it is high collapses into a lens, so a tiny value still shows one round drop.
            var size = Root.rect.size;
            float min = size.x > 1f ? Mathf.Clamp01(size.y / size.x) : 0f;
            _fillRect.anchorMax = new Vector2(Mathf.Max(value, min), 1f);
            bool visible = value > 0.004f;
            if (Fill.enabled != visible) Fill.enabled = visible;
        }

        internal void AttachShine(RectTransform shine)
        {
            _shine = shine;
            _shineRenderer = shine.GetComponent<CanvasRenderer>();
            _shine.gameObject.SetActive(false);
        }

        // A soft glint that wanders along the fill every few seconds; call once per frame with unscaled time.
        public void Animate(float time)
        {
            if (_shine == null) return;
            float phase = Mathf.Repeat(time / 3.2f, 1f) / 0.55f;
            bool on = phase < 1f && _value > 0.12f && Fill.enabled;
            if (_shine.gameObject.activeSelf != on) _shine.gameObject.SetActive(on);
            if (!on) return;
            float u = Mathf.SmoothStep(0.04f, 0.96f, phase);
            _shine.anchorMin = _shine.anchorMax = new Vector2(u, 0.5f);
            _shineRenderer.SetAlpha(Mathf.Sin(phase * Mathf.PI));
        }
    }

    // Row of soft circles; the current page is a wider mint pill.
    public sealed class UiPageDots
    {
        public readonly RectTransform Root;
        readonly Image[] _dots;
        readonly float _size, _pitch;
        int _count, _index = -1;

        internal UiPageDots(RectTransform root, Image[] dots, float size, float pitch)
        {
            Root = root;
            _dots = dots;
            _size = size;
            _pitch = pitch;
            _count = dots.Length;
        }

        public int Capacity => _dots.Length;

        public void Set(int index, int count = -1)
        {
            if (count < 0) count = _count;
            count = Mathf.Clamp(count, 0, _dots.Length);
            if (index == _index && count == _count) return;
            _index = index;
            _count = count;
            float width = (count - 1) * _pitch + _size * 1.5f;
            float x = -width * 0.5f;
            for (int i = 0; i < _dots.Length; i++)
            {
                bool shown = i < count, on = i == index;
                if (_dots[i].gameObject.activeSelf != shown) _dots[i].gameObject.SetActive(shown);
                if (!shown) continue;
                float w = on ? _size * 2.5f : _size;
                _dots[i].color = on ? UiStyle.Mint : UiStyle.Faint;
                _dots[i].rectTransform.anchoredPosition = new Vector2(x + w * 0.5f, 0f);
                _dots[i].rectTransform.sizeDelta = new Vector2(w, _size);
                x += w + (_pitch - _size);
            }
        }
    }

    // The one look of every Drift screen: palette, type scale, spacing, radii and the builders that apply them.
    // All sizes are canvas units of the 1080 x 1920 reference resolution.
    public static class UiStyle
    {
        public static readonly Color Glass = new Color(0.11f, 0.23f, 0.35f, 0.76f);
        public static readonly Color GlassDense = new Color(0.11f, 0.23f, 0.35f, 0.9f);
        public static readonly Color Veil = new Color(0.74f, 0.9f, 0.96f, 0.12f);
        public static readonly Color Ghost = new Color(0.99f, 0.96f, 0.88f, 0.14f);
        public static readonly Color Track = new Color(0.02f, 0.09f, 0.17f, 0.42f);
        public static readonly Color Line = new Color(0.99f, 0.96f, 0.88f, 0.13f);
        public static readonly Color Cream = new Color(1f, 0.97f, 0.9f, 1f);
        public static readonly Color CreamSoft = new Color(1f, 0.97f, 0.9f, 0.84f);
        public static readonly Color Muted = new Color(1f, 0.97f, 0.9f, 0.62f);
        public static readonly Color Faint = new Color(1f, 0.97f, 0.9f, 0.34f);
        public static readonly Color Mint = new Color(0.6f, 0.91f, 0.73f, 1f);
        public static readonly Color MintDeep = new Color(0.33f, 0.66f, 0.54f, 1f);
        public static readonly Color Ink = new Color(0.09f, 0.27f, 0.3f, 1f);
        public static readonly Color Lagoon = new Color(0.36f, 0.53f, 0.72f, 1f);
        public static readonly Color LagoonDeep = new Color(0.22f, 0.36f, 0.54f, 1f);
        public static readonly Color Rose = new Color(0.82f, 0.43f, 0.45f, 1f);
        public static readonly Color RoseDeep = new Color(0.62f, 0.28f, 0.33f, 1f);
        public static readonly Color CreamDeep = new Color(0.8f, 0.72f, 0.6f, 1f);
        public static readonly Color Sand = new Color(1f, 0.87f, 0.64f, 1f);
        public static readonly Color Coral = new Color(1f, 0.64f, 0.58f, 1f);
        public static readonly Color Sky = new Color(0.6f, 0.85f, 0.97f, 1f);
        public static readonly Color Dim = new Color(0.04f, 0.1f, 0.2f, 0.56f);
        public static readonly Color DimLight = new Color(0.04f, 0.1f, 0.2f, 0.28f);
        public static readonly Color Shadow = new Color(0.02f, 0.07f, 0.16f, 0.34f);
        public static readonly Color Sheen = new Color(1f, 0.99f, 0.95f, 0.1f);

        public const int Display = 156;
        public const int Title = 104;
        public const int Heading = 60;
        public const int Subheading = 44;
        public const int Body = 36;
        public const int Caption = 29;
        public const int ButtonText = 52;
        public const int ButtonTextSmall = 40;

        public const float Margin = 40f;
        public const float Pad = 40f;
        public const float Gap = 24f;
        public const float GapSmall = 12f;
        public const float PanelRadius = UiSprites.PanelRadius;
        public const float ChipRadius = UiSprites.CardRadius;
        public const float ButtonHeight = 140f;
        public const float ButtonHeightSmall = 96f;
        public const float LineSpacing = 1.22f;
        public const float ShadowDrop = 12f;

        public static readonly Vector2 Reference = new Vector2(1080f, 1920f);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ---------------------------------------------------------------- font

        public const string FontResource = "Fonts/DriftRounded";
        const string OsFontName = "DriftOsFont";
        // Round, friendly families first. System fonts are a development fallback only and never ship.
        static readonly string[] OsFonts =
        {
            "Arial Rounded MT Bold", "Varela Round", "Nunito", "Quicksand", "Comic Sans MS", "Segoe Print",
            "Noto Sans Rounded", "Rubik", "Segoe UI Variable Display", "Roboto",
        };
        // Asked per glyph after the main family, for symbols a display font lacks.
        static readonly string[] GlyphFallbacks =
        {
            "Segoe UI Symbol", "Segoe UI", "Arial", "Noto Sans Symbols", "Noto Sans", "Roboto", "DejaVu Sans", "Helvetica Neue", "Arial Unicode MS",
        };

        static Font _font;
        static float _lineScale = 1f;

        // Resources/Fonts/DriftRounded.ttf, else a rounded OS font, else Unity's builtin LegacyRuntime.
        public static Font Font => _font != null ? _font : (_font = ResolveFont());
        public static string FontSource { get; private set; } = "";
        // The bundled file is a variable font whose default instance is its Light weight: every label is emboldened.
        public static bool FontIsLight { get; private set; }

        public static FontStyle Weight(bool bold) => bold || (Font != null && FontIsLight) ? FontStyle.Bold : FontStyle.Normal;

        // Line spacing that gives the same line pitch for fonts with a taller line box than the reference 1.19 em.
        public static float Lines(float spacing)
        {
            var _ = Font;
            return spacing * _lineScale;
        }

        static Font ResolveFont()
        {
            FontIsLight = false;
            var font = Resources.Load<Font>(FontResource);
            if (font != null)
            {
                FontSource = "Resources/" + FontResource;
                FontIsLight = true;
                AddFallbacks(font);
            }
            else
            {
                font = CreateOsFont(out string picked);
                if (font != null) FontSource = "OS font " + picked;
                else
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    FontSource = "LegacyRuntime";
                }
            }
            float ratio = font != null && font.fontSize > 0 ? font.lineHeight / (float)font.fontSize : 1.19f;
            _lineScale = Mathf.Clamp(1.19f / Mathf.Max(0.8f, ratio), 0.75f, 1.1f);
            return font;
        }

        static void AddFallbacks(Font font)
        {
            var names = new List<string>(font.fontNames ?? new string[0]);
            bool changed = false;
            foreach (var n in GlyphFallbacks)
            {
                if (names.Contains(n)) continue;
                names.Add(n);
                changed = true;
            }
            if (changed) font.fontNames = names.ToArray();
        }

        public static Font CreateOsFont(out string picked)
        {
            picked = null;
            foreach (var existing in Resources.FindObjectsOfTypeAll<Font>())
                if (existing != null && existing.name == OsFontName)
                {
                    picked = existing.fontNames != null && existing.fontNames.Length > 0 ? existing.fontNames[0] : OsFontName;
                    return existing;
                }
            string[] installed;
            try { installed = Font.GetOSInstalledFontNames(); }
            catch (Exception) { installed = null; }
            if (installed == null || installed.Length == 0) return null;
            var have = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            foreach (var n in OsFonts) if (have.Contains(n)) names.Add(n);
            if (names.Count == 0) return null;
            picked = names[0];
            foreach (var n in GlyphFallbacks) if (!names.Contains(n)) names.Add(n);
            var font = Font.CreateDynamicFontFromOSFont(names.ToArray(), 32);
            if (font == null) return null;
            font.name = OsFontName;
            font.hideFlags = HideFlags.HideAndDontSave;
            return font;
        }

        // ---------------------------------------------------------------- canvas and placement

        public static Canvas Canvas(Transform parent, string name, int sortingOrder, bool raycaster, out RectTransform safeRoot)
        {
            var go = raycaster
                ? new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
                : new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            // Always overlay, never ScreenSpaceCamera: a camera canvas is drawn INTO the camera colour, so any
            // full-screen effect (post-processing, a filter feature) runs over the text as well and mangles it.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            safe.transform.SetParent(go.transform, false);
            safeRoot = safe.GetComponent<RectTransform>();
            Stretch(safeRoot);
            return canvas;
        }

        // Calls back whenever the rect changes size (rotation, split screen, an Edit Mode capture at another aspect).
        public static void OnResize(RectTransform rt, Action onResize)
        {
            var watcher = rt.GetComponent<UiResizeWatcher>();
            if (watcher == null) watcher = rt.gameObject.AddComponent<UiResizeWatcher>();
            watcher.Resized = onResize;
        }

        public static void DestroyChildrenNamed(Transform parent, string name)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (child.name != name) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // Anchored to the top centre of the parent; y counts downwards as a negative offset.
        public static RectTransform TopCenter(this RectTransform rt, Vector2 pos, Vector2 size) =>
            rt.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, size);

        public static RectTransform TopLeft(this RectTransform rt, Vector2 pos, Vector2 size) =>
            rt.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);

        public static RectTransform Center(this RectTransform rt, Vector2 pos, Vector2 size) =>
            rt.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

        public static RectTransform BottomCenter(this RectTransform rt, Vector2 pos, Vector2 size) =>
            rt.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), pos, size);

        public static RectTransform Stretch(this RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // ---------------------------------------------------------------- primitives

        public static Image Shape(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image Pill(Transform parent, string name, Vector2 size, Color color, bool raycast = false)
        {
            var img = Shape(parent, name, UiSprites.PillOf(Mathf.Min(size.x, size.y)), color, raycast);
            img.rectTransform.sizeDelta = size;
            return img;
        }

        public static Image Dot(Transform parent, string name, float size, Color color)
        {
            var img = Shape(parent, name, UiSprites.Circle, color);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        public static Image Icon(Transform parent, string name, UiIcon icon, float size, Color color)
        {
            var img = Shape(parent, name, UiSprites.Icon(icon), color);
            img.rectTransform.Center(Vector2.zero, new Vector2(size, size));
            return img;
        }

        // pillHeight > 0: the shadow of a pill of that height, else the shadow of a panel. lip: extra drop for a button's lip.
        static void AddShadow(RectTransform root, float strength = 1f, float pillHeight = 0f, float lip = 0f)
        {
            var sprite = pillHeight > 0f ? UiSprites.PillShadowOf(pillHeight) : UiSprites.SoftShadow;
            var shadow = Shape(root, "Shadow", sprite, WithAlpha(Shadow, Shadow.a * strength));
            float b = UiSprites.ShadowBlur;
            shadow.rectTransform.Stretch(-b, -b - ShadowDrop - lip, -b, -b + ShadowDrop);
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.UpperCenter, bool bold = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = Weight(bold);
            t.alignment = align;
            t.color = color;
            t.lineSpacing = Lines(LineSpacing);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            if (bold && FontIsLight && size >= ThickenFrom) Thicken(t, Mathf.Max(0.7f, size * 0.016f));
            return t;
        }

        const int ThickenFrom = 44;

        // Big headlines in the Light instance stay spindly even with synthetic bold; four offset copies in the
        // text's own colour make them chunky. Only for opaque colours: overlapping copies would add up alpha.
        public static void Thicken(Text t, float distance)
        {
            var outline = t.GetComponent<Outline>();
            if (outline == null) outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = t.color;
            outline.useGraphicAlpha = true;
            outline.effectDistance = new Vector2(distance, distance);
        }

        // Single-line label that never leaves its rect: whenever text, size or rect change it is scaled down around
        // its pivot until it fits (event driven through the dirty-vertices callback, nothing runs per frame).
        public static Text FitWidth(Text t, float minScale = 0.6f)
        {
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.RegisterDirtyVerticesCallback(() => ApplyFit(t, minScale));
            ApplyFit(t, minScale);
            return t;
        }

        static void ApplyFit(Text t, float minScale)
        {
            if (t == null) return;
            var rt = t.rectTransform;
            float width = rt.rect.width, scale = 1f;
            if (width > 1f && !string.IsNullOrEmpty(t.text))
            {
                float preferred = t.preferredWidth;
                if (preferred > width) scale = Mathf.Max(minScale, width / preferred);
            }
            if (Mathf.Abs(rt.localScale.x - scale) > 0.002f) rt.localScale = new Vector3(scale, scale, 1f);
        }

        // ---------------------------------------------------------------- surfaces

        // Full-screen dim layer that swallows clicks; panels of a modal screen are its children.
        public static RectTransform Scrim(Transform parent, string name, Color color)
        {
            var img = Shape(parent, name, null, color, true);
            return img.rectTransform.Stretch();
        }

        // Glass panel: soft shadow, rounded body, top sheen, gentle inner line. Content is parented to the returned root.
        public static RectTransform Panel(Transform parent, string name, Vector2 size, bool dense = false, bool blocksInput = true)
        {
            var root = Rect(parent, name);
            root.sizeDelta = size;
            AddShadow(root);
            Shape(root, "Body", UiSprites.RoundedLarge, dense ? GlassDense : Glass, blocksInput).rectTransform.Stretch();
            Shape(root, "Sheen", UiSprites.TopHighlight, Sheen).rectTransform.Stretch();
            float inset = UiSprites.PanelInset;
            Shape(root, "Border", UiSprites.PanelRing, WithAlpha(Cream, 0.09f)).rectTransform.Stretch(inset, inset, inset, inset);
            return root;
        }

        // Square glass frame with superellipse corners (minimap).
        public static RectTransform SquirclePanel(Transform parent, string name, float size)
        {
            var root = Rect(parent, name);
            root.sizeDelta = new Vector2(size, size);
            var shadow = Shape(root, "Shadow", UiSprites.SquircleShadow, Shadow);
            float grow = size * (1f / UiSprites.SquircleShadowBody - 1f) * 0.5f;
            shadow.rectTransform.Stretch(-grow, -grow - ShadowDrop, -grow, -grow + ShadowDrop);
            Shape(root, "Body", UiSprites.Squircle, Glass, true).rectTransform.Stretch();
            return root;
        }

        public static RectTransform Card(Transform parent, string name, Vector2 size)
        {
            var img = Shape(parent, name, UiSprites.RoundedFor(size), Veil);
            img.rectTransform.sizeDelta = size;
            return img.rectTransform;
        }

        public static RectTransform Chip(Transform parent, string name, string text, Vector2 size, out Text label)
        {
            var root = Rect(parent, name);
            root.sizeDelta = size;
            bool pill = size.y > 0f && size.y < 2f * UiSprites.CardRadius + 8f;
            AddShadow(root, 0.7f, pill ? size.y : 0f);
            Shape(root, "Body", UiSprites.RoundedFor(size), Glass).rectTransform.Stretch();
            label = Label(root, text, Caption, CreamSoft, TextAnchor.MiddleCenter);
            label.rectTransform.Stretch(Gap, 0f, Gap, 2f);
            FitWidth(label);
            return root;
        }

        public static RectTransform Divider(Transform parent, float width)
        {
            var img = Shape(parent, "Divider", UiSprites.PillOf(4f), Line);
            img.rectTransform.sizeDelta = new Vector2(width, 4f);
            return img.rectTransform;
        }

        // Inset track, glossy pill fill and a glint for UiBar.Animate.
        public static UiBar Bar(Transform parent, string name, Vector2 size, Color fill)
        {
            var track = Shape(parent, name, UiSprites.PillOf(size.y), Track);
            track.rectTransform.sizeDelta = size;
            var f = Shape(track.transform, "Fill", UiSprites.PillOf(size.y), fill);
            f.rectTransform.Stretch();
            Shape(f.transform, "Gloss", UiSprites.PillHighlightOf(size.y), WithAlpha(Color.white, 0.42f)).rectTransform.Stretch(2f, 2f, 2f, 1f);
            var shine = Shape(f.transform, "Shine", UiSprites.SoftCircle, WithAlpha(Color.white, 0.75f));
            shine.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.y * 2.6f, size.y * 0.8f));
            var bar = new UiBar(track.rectTransform, f);
            bar.AttachShine(shine.rectTransform);
            bar.Set(0f);
            return bar;
        }

        // Rounded window onto a texture (island picture, photos): soft inner shadow, and a rim that hides the stencil edge.
        public static RawImage Picture(Transform parent, string name, Vector2 size, out RectTransform frame)
        {
            return Picture(parent, name, size, false, out frame);
        }

        // squircle: superellipse window for square pictures (minimap).
        public static RawImage Picture(Transform parent, string name, Vector2 size, bool squircle, out RectTransform frame)
        {
            frame = Rect(parent, name);
            frame.sizeDelta = size;
            var mask = Shape(frame, "Mask", squircle ? UiSprites.Squircle : UiSprites.Rounded, WithAlpha(Track, 1f));
            mask.rectTransform.Stretch();
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var go = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(mask.transform, false);
            ((RectTransform)go.transform).Stretch();
            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            Shape(frame, "Shade", squircle ? UiSprites.SquircleInner : UiSprites.InnerShadow, WithAlpha(Shadow, 0.55f)).rectTransform.Stretch();
            Shape(frame, "Rim", squircle ? UiSprites.SquircleRing : UiSprites.Ring, WithAlpha(Cream, 0.3f)).rectTransform.Stretch();
            return raw;
        }

        // ---------------------------------------------------------------- buttons

        static float LipOf(float height) => Mathf.Clamp(Mathf.Round(height * 0.085f), 5f, 12f);

        // Candy button: soft shadow, darker lip under the body, top sheen and a little glint. The body is the target
        // graphic; UiPressFeedback squashes the whole control into its lip while it is held.
        static Button MakeButton(Transform parent, string name, Color bg, Color lipColor, Vector2 size, bool shadow, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Button), typeof(UiPressFeedback));
            go.transform.SetParent(parent, false);
            var root = (RectTransform)go.transform;
            root.sizeDelta = size;
            float h = size.y, lip = LipOf(h);
            if (shadow) AddShadow(root, 0.8f, h, lip);
            var lipImage = Shape(root, "Lip", UiSprites.PillOf(h), lipColor);
            lipImage.rectTransform.Stretch(0f, -lip, 0f, lip);
            var img = Shape(root, "Body", UiSprites.PillOf(h), bg, true);
            img.rectTransform.Stretch();
            var sheen = Shape(root, "Sheen", UiSprites.PillHighlightOf(h), WithAlpha(Color.white, 0.26f));
            sheen.rectTransform.Stretch(2f, 2f, 2f, 2f);
            var glint = Shape(root, "Glint", UiSprites.SoftCircle, WithAlpha(Color.white, 0.5f));
            glint.rectTransform.TopLeft(new Vector2(h * 0.3f, -h * 0.1f), new Vector2(h * 0.34f, h * 0.17f));
            // A caller may tint the body. A translucent tint (a quiet button on paper) would let the lip shine
            // through, so the candy parts step aside and the button becomes a flat ghost pill.
            img.RegisterDirtyVerticesCallback(() =>
            {
                if (img == null || lipImage == null) return;
                bool candy = img.color.a >= 0.6f;
                if (lipImage.enabled == candy) return;
                lipImage.enabled = sheen.enabled = glint.enabled = candy;
            });
            var press = go.GetComponent<UiPressFeedback>();
            press.lip = lipImage.rectTransform;
            press.lipDepth = lip;
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.88f, 0.9f, 0.9f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.62f, 0.66f, 0.7f, 1f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        static Text ButtonLabel(Button b, string label, Color color, int fontSize)
        {
            var t = Label(b.transform, label, fontSize, color, TextAnchor.MiddleCenter, true);
            t.rectTransform.Stretch(Gap, 0f, Gap, 4f);
            FitWidth(t);
            return t;
        }

        static int ButtonFont(Vector2 size) => size.y >= 120f ? ButtonText : ButtonTextSmall;

        public static Button PrimaryButton(Transform parent, string name, string label, Vector2 size, Action onClick)
        {
            var b = MakeButton(parent, name, Mint, MintDeep, size, true, onClick);
            ButtonLabel(b, label, Ink, ButtonFont(size));
            return b;
        }

        // glass: for buttons that float over the world instead of lying on a panel (they get a drop shadow).
        public static Button SecondaryButton(Transform parent, string name, string label, Vector2 size, Action onClick, bool warning = false, bool glass = false)
        {
            var b = MakeButton(parent, name, warning ? Rose : Lagoon, warning ? RoseDeep : LagoonDeep, size, glass, onClick);
            ButtonLabel(b, label, Cream, ButtonFont(size));
            return b;
        }

        // Round candy button; the caller draws the glyph into it.
        public static Button IconButton(Transform parent, string name, float size, Action onClick)
        {
            return MakeButton(parent, name, Lagoon, LagoonDeep, new Vector2(size, size), true, onClick);
        }

        public static Button IconButton(Transform parent, string name, float size, UiIcon icon, Action onClick)
        {
            var b = IconButton(parent, name, size, onClick);
            Icon(b.transform, "IconShade", icon, size * 0.56f, WithAlpha(Shadow, 0.3f)).rectTransform.anchoredPosition = new Vector2(0f, -size * 0.035f);
            Icon(b.transform, "Icon", icon, size * 0.56f, Cream);
            return b;
        }

        // Puts an icon in front of a text button's label; the label centres itself in the space that is left.
        public static Image ButtonIcon(Button b, UiIcon icon)
        {
            var root = (RectTransform)b.transform;
            float h = root.sizeDelta.y > 0f ? root.sizeDelta.y : root.rect.height;
            float size = h * 0.44f, x = h * 0.28f + size * 0.5f;
            var label = LabelOf(b);
            var img = Icon(root, "Icon", icon, size, label != null ? label.color : Cream);
            img.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(size, size));
            // On a wide button the label stays centred on the whole pill; a narrow one centres it in the space left.
            float width = root.sizeDelta.x > 0f ? root.sizeDelta.x : root.rect.width;
            float left = x + size * 0.5f + 8f;
            if (label != null) label.rectTransform.Stretch(left, 0f, width >= 480f ? left : Gap + 8f, 4f);
            return img;
        }

        public static Text LabelOf(Button b) => b != null ? b.GetComponentInChildren<Text>(true) : null;

        // Dims a whole candy button (lip, sheen and glyph too), which Button.interactable alone does not.
        public static void SetInteractable(Button b, bool on)
        {
            if (b == null) return;
            b.interactable = on;
            var group = b.GetComponent<CanvasGroup>();
            if (group == null) group = b.gameObject.AddComponent<CanvasGroup>();
            group.alpha = on ? 1f : 0.4f;
        }

        // Pill switch with a sliding knob and its caption on the left; the whole row is the click target.
        public static UiToggle Toggle(Transform parent, string name, string label, Vector2 size, bool on, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Button), typeof(UiToggle));
            go.transform.SetParent(parent, false);
            var root = (RectTransform)go.transform;
            root.sizeDelta = size;
            var body = Shape(root, "Body", UiSprites.PillOf(size.y), WithAlpha(Track, 0.5f), true);
            body.rectTransform.Stretch();

            float h = Mathf.Round(size.y * 0.66f), w = Mathf.Round(h * 1.9f), inset = (size.y - h) * 0.5f;
            var text = Label(root, label, size.y >= 96f ? ButtonTextSmall : Body, Cream, TextAnchor.MiddleLeft, true);
            text.rectTransform.Stretch(size.y * 0.42f, 0f, w + inset + Gap, 3f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.offsetMin = new Vector2(size.y * 0.42f, 0f);
            text.rectTransform.offsetMax = new Vector2(-(w + inset + Gap), -3f);
            FitWidth(text);

            var track = Shape(root, "Track", UiSprites.PillOf(h), Track);
            track.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-inset, 0f), new Vector2(w, h));
            var tint = Shape(track.transform, "On", UiSprites.PillOf(h), Mint);
            tint.rectTransform.Stretch();
            Shape(track.transform, "Shade", UiSprites.PillHighlightOf(h), WithAlpha(Shadow, 0.5f)).rectTransform.Stretch();
            float k = h - 10f;
            var knob = Rect(track.transform, "Knob").Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(k, k));
            Shape(knob, "Lip", UiSprites.Circle, CreamDeep).rectTransform.Stretch(0f, -4f, 0f, 4f);
            Shape(knob, "Body", UiSprites.Circle, Cream).rectTransform.Stretch();
            Shape(knob, "Glint", UiSprites.SoftCircle, WithAlpha(Color.white, 0.9f)).rectTransform.Center(new Vector2(-k * 0.12f, k * 0.16f), new Vector2(k * 0.42f, k * 0.3f));

            var b = go.GetComponent<Button>();
            b.targetGraphic = body;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = b.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(1f, 1f, 1f, 1.5f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());

            var toggle = go.GetComponent<UiToggle>();
            toggle.Bind(text, knob, tint, h * 0.5f, w - h * 0.5f);
            toggle.Set(on, false);
            return toggle;
        }

        public static UiPageDots PageDots(Transform parent, string name, int count, float size = 22f)
        {
            float pitch = size + 20f;
            var root = Rect(parent, name);
            root.sizeDelta = new Vector2(count * pitch + size * 1.5f, size + 8f);
            var dots = new Image[count];
            for (int i = 0; i < count; i++)
            {
                dots[i] = Shape(root, "Dot" + i, UiSprites.PillOf(size), Faint);
                dots[i].rectTransform.Center(Vector2.zero, new Vector2(size, size));
            }
            var pages = new UiPageDots(root, dots, size, pitch);
            pages.Set(0, count);
            return pages;
        }

        // ---------------------------------------------------------------- glyphs

        public static RectTransform KeyCap(Transform parent, string text, Vector2 size)
        {
            var root = Rect(parent, "Key" + text);
            root.sizeDelta = size;
            var sprite = UiSprites.RoundedOf(Mathf.RoundToInt(Mathf.Min(UiSprites.SmallRadius, Mathf.Min(size.x, size.y) * 0.34f)));
            var lip = Shape(root, "Lip", sprite, CreamDeep);
            lip.rectTransform.Stretch(0f, -8f, 0f, 8f);
            Shape(root, "Body", sprite, Cream).rectTransform.Stretch();
            var t = Label(root, text, text.Length > 1 ? 30 : 38, Ink, TextAnchor.MiddleCenter, true);
            t.rectTransform.Stretch(6f, 0f, 6f, 2f);
            FitWidth(t);
            return root;
        }

        // ---------------------------------------------------------------- motion

        public static void FadeIn(GameObject screen, RectTransform slide)
        {
            var fade = screen.GetComponent<UiFadeIn>();
            if (fade == null) fade = screen.AddComponent<UiFadeIn>();
            fade.slide = slide;
        }
    }
}
