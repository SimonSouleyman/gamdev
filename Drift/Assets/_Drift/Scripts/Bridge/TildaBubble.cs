using System;
using System.Collections.Generic;
using System.Text;
using Drift.Audio;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Tilda's speech bubble: solid cream paper with dark ink type, a thick outline on a soft halo and drop shadow, a
    // comic tail aimed at her mouth, her name on a chip. It types the text at the pace her grumble mumbles it
    // (GrumbleTiming is the shared clock), splits long texts into pages of about four lines, pops in a little with
    // every page and grows to the height of its text. Built from UiStyle helpers and UiStyle.Font only, so a change
    // of the UI font reaches it; the tail sprite is generated here. The rich-text helpers are pure.
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class TildaBubble : MonoBehaviour
    {
        public const int BodySize = 40, NameSize = UiStyle.Subheading;
        public const float PadX = 46f, PadTop = 64f, PadBottom = 42f, FooterHeight = 84f, FooterGap = 26f;
        public const float OutlineWidth = 7f, TailWidth = 70f, TailInset = 14f, MinTail = 34f, MaxTail = 150f;
        public const float PopSeconds = 0.2f, RemarkCharsPerSecond = 40f;
        // The name chip sticks out over the top edge by this much.
        public const float NameOverhang = 40f;
        // A remark beside a menu is set in a column of about this many characters per page.
        public const int RemarkMaxChars = 92;
        public const string KeyColor = "#B23F14";

        public static readonly Color Paper = new Color(1f, 0.975f, 0.905f, 0.98f);
        public static readonly Color InkText = new Color(0.17f, 0.1f, 0.09f, 1f);
        public static readonly Color InkSoft = new Color(0.17f, 0.1f, 0.09f, 0.62f);
        public static readonly Color OutlineInk = new Color(0.15f, 0.09f, 0.09f, 0.96f);
        public static readonly Color Halo = new Color(0.04f, 0.03f, 0.05f, 0.5f);
        public static readonly Color ChipColor = new Color(0.78f, 0.29f, 0.12f, 1f);

        // Unscaled time of the last frame in which any bubble was still typing: her mouth moves with the text.
        static float s_typingAt = -10f;
        public static bool TypingNow => Application.isPlaying && Time.unscaledTime - s_typingAt < 0.12f;

        // ---------------------------------------------------------------- rich text (pure)

        // Marks a key word: bold in warm lava ink. Use sparingly, one or two per text.
        public static string Key(string word) => "<b><color=" + KeyColor + ">" + word + "</color></b>";

        static int TagLength(string s, int i, out bool color, out bool closing)
        {
            color = closing = false;
            if (i >= s.Length || s[i] != '<') return 0;
            int end = s.IndexOf('>', i);
            if (end < 0 || end - i > 24) return 0;
            closing = i + 1 < s.Length && s[i + 1] == '/';
            int nameAt = i + (closing ? 2 : 1);
            if (string.CompareOrdinal(s, nameAt, "color", 0, 5) == 0) color = true;
            else if (!(end == nameAt + 1 && (s[nameAt] == 'b' || s[nameAt] == 'i'))) return 0;
            return end - i + 1;
        }

        public static string Plain(string rich)
        {
            if (string.IsNullOrEmpty(rich) || rich.IndexOf('<') < 0) return rich ?? "";
            var sb = new StringBuilder(rich.Length);
            for (int i = 0; i < rich.Length;)
            {
                int tag = TagLength(rich, i, out _, out _);
                if (tag > 0) i += tag;
                else sb.Append(rich[i++]);
            }
            return sb.ToString();
        }

        // The text with everything after the first `shown` visible characters made transparent, so lines never
        // re-wrap while she talks. Colour tags inside the hidden rest are dropped (they would show through).
        static readonly StringBuilder s_typed = new StringBuilder(512);
        static readonly List<char> s_open = new List<char>(4);

        public static string Typed(string rich, int shown)
        {
            if (string.IsNullOrEmpty(rich)) return "";
            var sb = s_typed;
            sb.Length = 0;
            // Open tags, innermost last: 'c' colour, else the tag's letter.
            var open = s_open;
            open.Clear();
            bool hidden = false;
            int plain = 0;
            for (int i = 0; i < rich.Length;)
            {
                int tag = TagLength(rich, i, out bool color, out bool closing);
                if (tag > 0)
                {
                    char kind = color ? 'c' : rich[i + (closing ? 2 : 1)];
                    if (!closing) open.Add(kind);
                    else if (open.Count > 0) open.RemoveAt(open.Count - 1);
                    if (!color || !hidden) sb.Append(rich, i, tag);
                    i += tag;
                    continue;
                }
                if (!hidden && plain >= shown)
                {
                    hidden = true;
                    for (int k = open.Count - 1; k >= 0; k--) sb.Append(open[k] == 'c' ? "</color>" : "</" + open[k] + ">");
                    sb.Append("<color=#00000000>");
                    for (int k = 0; k < open.Count; k++) if (open[k] != 'c') sb.Append('<').Append(open[k]).Append('>');
                }
                sb.Append(rich[i++]);
                plain++;
            }
            if (!hidden) return rich;
            sb.Append("</color>");
            return sb.ToString();
        }

        // Splits a text into pages of at most maxChars visible characters, at sentence ends where possible, else
        // at a space; never inside a tag pair.
        public static List<string> Pages(string rich, int maxChars)
        {
            var pages = new List<string>();
            if (string.IsNullOrEmpty(rich)) return pages;
            maxChars = Mathf.Max(20, maxChars);
            int pageStart = 0;
            while (pageStart < rich.Length)
            {
                int depth = 0, plain = 0, sentenceBreak = -1, spaceBreak = -1, i = pageStart;
                bool over = false;
                while (i < rich.Length)
                {
                    int tag = TagLength(rich, i, out _, out bool closing);
                    if (tag > 0)
                    {
                        depth += closing ? -1 : 1;
                        i += tag;
                        continue;
                    }
                    plain++;
                    if (plain > maxChars && (sentenceBreak > pageStart || spaceBreak > pageStart)) { over = true; break; }
                    if (rich[i] == ' ' && depth == 0)
                    {
                        spaceBreak = i + 1;
                        if (i > 0 && (GrumbleTiming.EndsSentence(rich[i - 1]) || rich[i - 1] == '“')) sentenceBreak = i + 1;
                    }
                    i++;
                }
                if (!over)
                {
                    pages.Add(rich.Substring(pageStart).Trim());
                    break;
                }
                // A sentence break is only worth it when the page does not get too short.
                int cut = sentenceBreak > pageStart && PlainLength(rich, pageStart, sentenceBreak) >= maxChars * 0.4f ? sentenceBreak : spaceBreak;
                pages.Add(rich.Substring(pageStart, cut - pageStart).Trim());
                pageStart = cut;
            }
            return pages;
        }

        static int PlainLength(string rich, int from, int to)
        {
            int n = 0;
            for (int i = from; i < to;)
            {
                int tag = TagLength(rich, i, out _, out _);
                if (tag > 0) i += tag;
                else { n++; i++; }
            }
            return n;
        }

        // Reading time after a page has been typed.
        public static float HoldSeconds(int plainChars) => 1.3f + 0.042f * plainChars;

        // How long a remark is up when its pages turn by themselves.
        public static float SecondsFor(string rich, int maxChars = RemarkMaxChars, float charsPerSecond = RemarkCharsPerSecond)
        {
            float total = 0f;
            foreach (var page in Pages(rich, maxChars))
            {
                string plain = Plain(page);
                total += GrumbleTiming.Duration(plain, charsPerSecond) + HoldSeconds(plain.Length);
            }
            return total;
        }

        public static int MaxCharsFor(float bubbleWidth) =>
            Mathf.Max(40, Mathf.FloorToInt((bubbleWidth - 2f * PadX) / (BodySize * 0.5f) * 4f * 0.84f));

        // ---------------------------------------------------------------- tail sprite

        static Sprite s_tail;

        // A slightly bent comic tail, base at the top edge, tip at the bottom; white, tinted by the Image.
        static Sprite TailSprite
        {
            get
            {
                if (s_tail != null) return s_tail;
                const int w = 96, h = 160;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "TildaBubbleTail", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;
                    float half = 0.47f * Mathf.Pow(v, 0.8f) * w;
                    float centre = (0.5f + 0.2f * (1f - v) * (1f - v)) * w;
                    for (int x = 0; x < w; x++)
                    {
                        float a = Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - centre) + 0.5f) * Mathf.Clamp01(v * h * 0.5f);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_tail = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f);
                s_tail.name = "TildaBubbleTail";
                s_tail.hideFlags = HideFlags.HideAndDontSave;
                return s_tail;
            }
        }

        // ---------------------------------------------------------------- component

        RectTransform _rect, _tail, _tailOutline, _textRect, _tapRect;
        Text _text, _counter, _hint, _nextLabel;
        Button _next;
        GameObject _nextArrow;
        bool _footer;
        float _width;

        readonly List<string> _pages = new List<string>();
        string _pageRich = "", _pagePlain = "", _finalLabel;
        Action _onFinal, _onFinished;
        int _page, _shown = -1;
        float _pageTime, _charsPerSecond = RemarkCharsPerSecond, _pop = 1f;
        bool _auto, _still, _finished, _grumbles = true;
        int _rounds;
        GrumbleMood _mood;
        Vector3 _tailTarget;
        float _tailGap = 40f, _placedGap;
        Vector2 _placedTarget, _placedSize;
        bool _tailPlaced;
        bool _hasTail;

        public RectTransform Rect => _rect;
        public float Width => _width;
        public float Height => _rect != null ? _rect.sizeDelta.y : 0f;
        public int Page => _page;
        public int PageCount => _pages.Count;
        public bool LastPage => _page >= _pages.Count - 1;
        public bool Typing => !_still && _shown < _pagePlain.Length;
        // All pages shown and read (pages that turn by themselves only).
        public bool Finished => _finished;
        public Text BodyText => _text;
        public Button NextButton => _next;
        public RectTransform Footer { get; private set; }

        // footer: room for buttons under the text (the tutorial); remarks beside the menus have none.
        public static TildaBubble Create(RectTransform parent, string name, float width, bool footer)
        {
            var rect = UiStyle.Rect(parent, name);
            var b = rect.gameObject.AddComponent<TildaBubble>();
            b._rect = rect;
            b._width = width;
            b._footer = footer;
            rect.sizeDelta = new Vector2(width, 300f);

            var shadow = UiStyle.Shape(rect, "Shadow", UiSprites.SoftShadow, new Color(0.01f, 0.03f, 0.08f, 0.5f));
            float blur = UiSprites.ShadowBlur;
            shadow.rectTransform.Stretch(-blur, -blur - 20f, -blur, -blur + 20f);
            var halo = UiStyle.Shape(rect, "Halo", UiSprites.SoftShadow, Halo);
            halo.rectTransform.Stretch(-blur + 8f, -blur + 8f, -blur + 8f, -blur + 8f);

            b._tailOutline = UiStyle.Shape(rect, "TailOutline", TailSprite, OutlineInk).rectTransform;
            UiStyle.Shape(rect, "Outline", UiSprites.RoundedLarge, OutlineInk).rectTransform.Stretch(-OutlineWidth, -OutlineWidth, -OutlineWidth, -OutlineWidth);
            UiStyle.Shape(rect, "Body", UiSprites.RoundedLarge, Paper).rectTransform.Stretch();
            b._tail = UiStyle.Shape(rect, "Tail", TailSprite, new Color(Paper.r, Paper.g, Paper.b, 1f)).rectTransform;
            b._tail.gameObject.SetActive(false);
            b._tailOutline.gameObject.SetActive(false);

            var tap = UiStyle.Shape(rect, "Tap", null, new Color(1f, 1f, 1f, 0f), true);
            b._tapRect = tap.rectTransform.Stretch();
            var tapButton = tap.gameObject.AddComponent<Button>();
            tapButton.transition = Selectable.Transition.None;
            tapButton.navigation = new Navigation { mode = Navigation.Mode.None };
            tapButton.onClick.AddListener(() => b.Next());

            var chipRim = UiStyle.Pill(rect, "NameRim", new Vector2(188f, 76f), OutlineInk);
            chipRim.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(PadX - 16f, -4f), new Vector2(188f, 76f));
            var chip = UiStyle.Pill(rect, "Name", new Vector2(176f, 64f), ChipColor);
            chip.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(PadX - 10f, -4f), new Vector2(176f, 64f));
            var name44 = UiStyle.Label(chip.transform, "Tilda", NameSize, UiStyle.Cream, TextAnchor.MiddleCenter, true);
            name44.rectTransform.Stretch(0f, 0f, 0f, 3f);
            name44.horizontalOverflow = HorizontalWrapMode.Overflow;

            b._counter = UiStyle.Label(rect, "", UiStyle.Caption, InkSoft, TextAnchor.MiddleRight);
            b._counter.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-PadX, -14f), new Vector2(220f, 44f));
            b._counter.horizontalOverflow = HorizontalWrapMode.Overflow;

            b._text = UiStyle.Label(rect, "", BodySize, InkText, TextAnchor.UpperLeft);
            b._text.supportRichText = true;
            b._textRect = b._text.rectTransform;
            b._textRect.TopLeft(new Vector2(PadX, -PadTop), new Vector2(width - 2f * PadX, 200f));

            if (footer)
            {
                b.Footer = UiStyle.Rect(rect, "Footer");
                b.Footer.anchorMin = new Vector2(0f, 0f);
                b.Footer.anchorMax = new Vector2(1f, 0f);
                b.Footer.pivot = new Vector2(0.5f, 0f);
                b.Footer.offsetMin = new Vector2(PadX, PadBottom - 12f);
                b.Footer.offsetMax = new Vector2(-PadX + 12f, PadBottom - 12f + FooterHeight);
                b._hint = UiStyle.Label(b.Footer, "", UiStyle.Caption, InkSoft, TextAnchor.MiddleLeft);
                b._hint.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(width * 0.5f, FooterHeight));

                b._next = UiStyle.PrimaryButton(b.Footer, "BubbleNext", "Weiter", new Vector2(268f, FooterHeight), () => b.Next());
                ((RectTransform)b._next.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(268f, FooterHeight));
                b._nextLabel = UiStyle.LabelOf(b._next);
                var arrow = UiStyle.Shape(b._next.transform, "Arrow", UiSprites.Arrow, b._nextLabel != null ? b._nextLabel.color : UiStyle.Ink);
                arrow.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(34f, 34f));
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                b._nextArrow = arrow.gameObject;
            }
            b.Layout();
            return b;
        }

        // A secondary button on the paper (ink instead of cream): the tutorial's "Überspringen".
        public Button AddFooterButton(string name, string label, float width, float rightOffset, Action onClick)
        {
            if (Footer == null) return null;
            var button = UiStyle.SecondaryButton(Footer, name, label, new Vector2(width, FooterHeight - 12f), onClick);
            ((RectTransform)button.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-rightOffset, 0f), new Vector2(width, FooterHeight - 12f));
            if (button.targetGraphic != null) button.targetGraphic.color = new Color(InkText.r, InkText.g, InkText.b, 0.13f);
            var text = UiStyle.LabelOf(button);
            if (text != null)
            {
                text.color = new Color(InkText.r, InkText.g, InkText.b, 0.8f);
                text.fontSize = UiStyle.Caption + 2;
                text.fontStyle = FontStyle.Bold;
            }
            return button;
        }

        public void SetCounter(string text)
        {
            if (_counter != null) _counter.text = text ?? "";
        }

        public void SetHint(string text, float width)
        {
            if (_hint == null) return;
            _hint.text = text ?? "";
            _hint.rectTransform.sizeDelta = new Vector2(width, FooterHeight);
        }

        // What the button does on the last page; label null: no button there.
        public void SetFinal(string label, Action onClick)
        {
            _finalLabel = label;
            _onFinal = onClick;
            RefreshButton();
        }

        // Taps count from this distance to the bubble's left edge on (the tutorial keeps the thumbstick's half free).
        public void SetTapFrom(float left)
        {
            if (_tapRect == null) return;
            _tapRect.offsetMin = new Vector2(Mathf.Clamp(left, 0f, _width), 0f);
        }

        public void SetWidth(float width)
        {
            if (Mathf.Approximately(width, _width)) return;
            _width = width;
            _textRect.sizeDelta = new Vector2(width - 2f * PadX, _textRect.sizeDelta.y);
            Layout();
        }

        // Types the text page by page and grumbles along. autoAdvance: pages turn by themselves after a reading
        // pause and onFinished is called after the last one; otherwise Next() turns them.
        public void Show(string rich, GrumbleMood mood, float charsPerSecond, bool autoAdvance, Action onFinished = null, int maxChars = 0, bool grumbles = true)
        {
            _pages.Clear();
            _pages.AddRange(Pages(rich ?? "", maxChars > 0 ? maxChars : MaxCharsFor(_width)));
            if (_pages.Count == 0) _pages.Add("");
            _mood = mood;
            _charsPerSecond = Mathf.Max(1f, charsPerSecond);
            _auto = autoAdvance;
            _onFinished = onFinished;
            _grumbles = grumbles;
            _rounds = 0;
            _finished = false;
            _still = !Application.isPlaying;
            BeginPage(0);
        }

        // Edit Mode previews: the first page, fully typed.
        public void ShowStill(string rich, int maxChars = 0)
        {
            Show(rich, GrumbleMood.Warm, RemarkCharsPerSecond, false, null, maxChars, false);
            _still = true;
            _pop = 1f;
            _rect.localScale = Vector3.one;
            ShowTyped(_pagePlain.Length);
        }

        void BeginPage(int page)
        {
            _page = Mathf.Clamp(page, 0, _pages.Count - 1);
            _pageRich = _pages[_page];
            _pagePlain = Plain(_pageRich);
            _pageTime = 0f;
            _shown = -1;
            _pop = _still ? 1f : 0f;
            ShowTyped(_still ? _pagePlain.Length : 0);
            Layout();
            RefreshButton();
            // Pages that come round again (a step that waits for the player) are typed in silence.
            if (!_still && _grumbles && _rounds == 0 && _pagePlain.Length > 0) TildaVoice.Murmur(_pagePlain, _mood, _charsPerSecond);
        }

        // Finishes the typing, else turns the page, else runs the final action. False when nothing was left to do.
        public bool Next()
        {
            if (Typing)
            {
                ShowTyped(_pagePlain.Length);
                _pageTime = GrumbleTiming.Duration(_pagePlain, _charsPerSecond);
                if (_grumbles) TildaVoice.Quiet();
                return true;
            }
            if (!LastPage)
            {
                BeginPage(_page + 1);
                return true;
            }
            if (_auto && !_finished)
            {
                Finish();
                return true;
            }
            if (_onFinal == null) return false;
            _onFinal();
            return true;
        }

        public void Restart()
        {
            if (_pages.Count > 0) BeginPage(0);
        }

        void Finish()
        {
            _finished = true;
            var done = _onFinished;
            _onFinished = null;
            done?.Invoke();
        }

        void ShowTyped(int chars)
        {
            chars = Mathf.Clamp(chars, 0, _pagePlain.Length);
            if (chars == _shown) return;
            _shown = chars;
            _text.text = Typed(_pageRich, chars);
        }

        void RefreshButton()
        {
            if (_next == null) return;
            bool paging = !LastPage;
            bool visible = paging || _finalLabel != null;
            if (_next.gameObject.activeSelf != visible) _next.gameObject.SetActive(visible);
            if (!visible) return;
            if (_nextLabel != null)
            {
                _nextLabel.text = paging ? "Weiter" : _finalLabel;
                _nextLabel.rectTransform.offsetMax = new Vector2(paging ? -58f : -UiStyle.Gap, _nextLabel.rectTransform.offsetMax.y);
            }
            if (_nextArrow != null && _nextArrow.activeSelf != paging) _nextArrow.SetActive(paging);
        }

        void Update()
        {
            if (!Application.isPlaying || _still) return;
            float dt = Time.unscaledDeltaTime;
            if (_pop < 1f)
            {
                _pop = Mathf.Min(1f, _pop + dt / PopSeconds);
                float u = _pop - 1f;
                float eased = 1f + 2.4f * u * u * u + 1.4f * u * u;
                _rect.localScale = Vector3.one * (0.9f + 0.1f * eased);
            }
            _pageTime += dt;
            if (Typing)
            {
                ShowTyped(GrumbleTiming.CharsAt(_pagePlain, _pageTime, _charsPerSecond));
                s_typingAt = Time.unscaledTime;
            }
            else if (_auto && !_finished && _pageTime >= GrumbleTiming.Duration(_pagePlain, _charsPerSecond) + HoldSeconds(_pagePlain.Length))
            {
                if (!LastPage) BeginPage(_page + 1);
                else if (_onFinished == null && _pages.Count > 1)
                {
                    _rounds++;
                    BeginPage(0);
                }
                else Finish();
            }
            if (_hasTail) PlaceTail();
        }

        void OnDisable()
        {
            if (_rect != null) _rect.localScale = Vector3.one;
            _pop = 1f;
        }

        // ---------------------------------------------------------------- layout

        public void Layout()
        {
            if (_rect == null || _text == null) return;
            float textHeight = Mathf.Max(BodySize * 1.3f, LayoutUtility.GetPreferredHeight(_textRect));
            _textRect.sizeDelta = new Vector2(_width - 2f * PadX, textHeight);
            float height = PadTop + textHeight + (_footer ? FooterGap + FooterHeight + PadBottom - 12f : PadBottom);
            if (!Mathf.Approximately(height, _rect.sizeDelta.y) || !Mathf.Approximately(_width, _rect.sizeDelta.x))
                _rect.sizeDelta = new Vector2(_width, height);
            if (_hasTail) PlaceTail();
        }

        // Aims the tail at a point in world space (her mouth). The base slides along the edge that faces the point.
        // gap: the tip stops this far short of the point.
        public void PointTailAt(Vector3 world, float gap = 40f)
        {
            _tailTarget = world;
            _tailGap = gap;
            _hasTail = true;
            if (!_tail.gameObject.activeSelf) _tail.gameObject.SetActive(true);
            if (!_tailOutline.gameObject.activeSelf) _tailOutline.gameObject.SetActive(true);
            PlaceTail();
        }

        public void HideTail()
        {
            _hasTail = false;
            _tailPlaced = false;
            if (_tail.gameObject.activeSelf) _tail.gameObject.SetActive(false);
            if (_tailOutline.gameObject.activeSelf) _tailOutline.gameObject.SetActive(false);
        }

        public static void TailGeometry(Rect box, Vector2 target, float gap, out Vector2 basePoint, out float angle, out float length)
        {
            float corner = UiStyle.PanelRadius + TailWidth * 0.5f;
            float dxl = box.xMin - target.x, dxr = target.x - box.xMax, dyb = box.yMin - target.y, dyt = target.y - box.yMax;
            float best = Mathf.Max(Mathf.Max(dxl, dxr), Mathf.Max(dyb, dyt));
            float cx = Mathf.Clamp(target.x, Mathf.Min(box.xMin + corner, box.center.x), Mathf.Max(box.xMax - corner, box.center.x));
            float cy = Mathf.Clamp(target.y, Mathf.Min(box.yMin + corner, box.center.y), Mathf.Max(box.yMax - corner, box.center.y));
            if (best == dxl) basePoint = new Vector2(box.xMin + TailInset, cy);
            else if (best == dxr) basePoint = new Vector2(box.xMax - TailInset, cy);
            else if (best == dyb) basePoint = new Vector2(cx, box.yMin + TailInset);
            else basePoint = new Vector2(cx, box.yMax - TailInset);
            Vector2 d = target - basePoint;
            length = Mathf.Clamp(d.magnitude - gap, MinTail + TailInset, MaxTail + TailInset);
            angle = d.sqrMagnitude < 1e-4f ? 0f : Vector2.SignedAngle(Vector2.down, d);
        }

        void PlaceTail()
        {
            Vector2 target = _rect.InverseTransformPoint(_tailTarget);
            Vector2 size = _rect.rect.size;
            if (_tailPlaced && (target - _placedTarget).sqrMagnitude < 0.25f && (size - _placedSize).sqrMagnitude < 0.25f && Mathf.Approximately(_placedGap, _tailGap)) return;
            _tailPlaced = true;
            _placedTarget = target;
            _placedSize = size;
            _placedGap = _tailGap;
            TailGeometry(_rect.rect, target, _tailGap, out var basePoint, out float angle, out float length);
            var rot = Quaternion.Euler(0f, 0f, angle);
            var anchor = new Vector2(_rect.pivot.x, _rect.pivot.y);
            _tail.Place(anchor, new Vector2(0.5f, 1f), basePoint, new Vector2(TailWidth, length));
            _tail.localRotation = rot;
            _tailOutline.Place(anchor, new Vector2(0.5f, 1f), basePoint, new Vector2(TailWidth + 2.4f * OutlineWidth, length + 2.1f * OutlineWidth));
            _tailOutline.localRotation = rot;
        }
    }
}
