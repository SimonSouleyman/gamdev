using System;
using System.Collections.Generic;
using System.Globalization;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // "Durchgangs-Tagebuch": the keepsake book of finished Pangäas. Opens on RunJournal.OpenRequested over the title,
    // the pause menu and the finale (its own canvas sorts above them) and pauses nothing. A scrolling list of cards,
    // newest first, one column in portrait and two in landscape; a card opens its space picture in a full view.
    // Pictures are loaded only for the cards around the visible part of the list, one per frame.
    [ExecuteAlways]
    public class RunJournalPanel : MonoBehaviour
    {
        public enum EditorPreview { None, List, Empty, Picture }

        const string CanvasName = "RunJournalCanvas";
        public const float WideFrom = 2100f;
        const float CardH = 310f, CardGap = 20f, PicH = 270f, PicW = 480f, CardPad = 20f, MaxFit = 1.5f;
        const float HeaderH = 204f, FooterH = 36f + 124f + 24f;
        const int LoadAhead = 1, KeepAhead = 3;
        static readonly Vector2 TallSize = new Vector2(960f, 1680f), WideSize = new Vector2(1980f, 1080f);
        static readonly string[] Months =
        {
            "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
        };

        static readonly NumberFormatInfo GermanNumbers = new NumberFormatInfo { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

        [Header("Darstellung")]
        [Tooltip("Zeichenreihenfolge der eigenen Leinwand. Muss über Titel (10), Pausenmenü, Beobachten (15), Touch-Steuerung (20) und dem Pangäa-Finale liegen.")]
        public int sortingOrder = 40;
        [Tooltip("Vorschau im Editor ohne Play Mode, mit erfundenen Seiten (liest und schreibt nichts).")]
        public EditorPreview editorPreview = EditorPreview.None;

        sealed class CardView
        {
            public RectTransform root;
            public RawImage picture;
            public Image placeholder;
            public Text title, date;
            public readonly Text[] values = new Text[StatCount];
            public Texture2D texture;
            public bool tried;
            public int record = -1;
        }

        const int StatCount = 5;
        static readonly string[] StatNames = { "Spielzeit", "Inseln vereint", "Landmasse", "Arten gesehen", "Fotos" };

        static RunJournalPanel s_active;
        static int s_closedFrame = -1;

        Canvas _canvas;
        RectTransform _root, _screen, _fit, _panel, _list, _content, _empty, _viewFrame;
        GameObject _view;
        Text _title, _count, _viewTitle, _viewDate, _viewStats;
        Button _close, _viewBack;
        ScrollRect _scroll;
        RawImage _viewPicture;
        Texture2D _viewTexture;
        readonly List<CardView> _cards = new List<CardView>();
        readonly List<RunRecord> _sample = new List<RunRecord>();
        IReadOnlyList<RunRecord> _records;
        bool _open, _usingSample, _dirty;
        int _wide = -1, _columns = 1, _viewIndex = -1;
        Vector2 _viewFittedFor;
        EditorPreview _previewShown = EditorPreview.None;

        // True while the run journal is open, and in the frame it was closed with Escape / Android back: other
        // screens skip their own back handling then, whichever of them runs first in that frame.
        public static bool OwnsBack => (s_active != null && s_active._open) || s_closedFrame == Time.frameCount;
        public static bool AnyOpen => s_active != null && s_active._open;

        public bool IsOpen => _open;
        public bool ViewingPicture => _view != null && _view.activeSelf;
        public int CardCount => _records != null ? _records.Count : 0;
        public int LoadedPictures
        {
            get
            {
                int n = 0;
                foreach (var c in _cards) if (c.texture != null) n++;
                return n;
            }
        }
        public GameObject Root => _screen != null ? _screen.gameObject : null;
        public bool Wide => _wide == 1;

        // Without a component in the scene the game still gets its run journal.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (FindAnyObjectByType<RunJournalPanel>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("RunJournal");
            go.AddComponent<RunJournalPanel>();
            DontDestroyOnLoad(go);
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            s_active = this;
            Build();
            RunJournal.OpenRequested += OnOpenRequested;
            RunJournal.Changed += OnJournalChanged;
        }

        void OnDisable()
        {
            RunJournal.OpenRequested -= OnOpenRequested;
            RunJournal.Changed -= OnJournalChanged;
            Close();
            ReleaseSample();
            _previewShown = EditorPreview.None;
            if (s_active == this) s_active = null;
        }

        void OnOpenRequested() => Open();

        void OnJournalChanged()
        {
            if (_open && !_usingSample) _dirty = true;
        }

        void Update()
        {
            if (_canvas == null) return;
            if (!Application.isPlaying)
            {
                EditorPreviewUpdate();
                return;
            }
            if (!_open) return;
            if (_dirty) Refill();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Back();
            if (!_open) return;
            Tick();
        }

        void EditorPreviewUpdate()
        {
            if (editorPreview == _previewShown)
            {
                if (_open) Tick();
                return;
            }
            _previewShown = editorPreview;
            Close();
            if (editorPreview == EditorPreview.None) return;
            OpenSample(editorPreview == EditorPreview.Empty ? 0 : 7);
            if (editorPreview == EditorPreview.Picture) ShowPicture(0);
        }

        // ---------------------------------------------------------------- control

        public void Open()
        {
            if (_canvas == null) Build();
            _usingSample = false;
            _records = RunJournal.Records;
            Show();
        }

        // Edit Mode preview and tests: made-up records with generated pictures, nothing is read or written.
        public void OpenSample(int count)
        {
            if (_canvas == null) Build();
            ReleaseSample();
            var t = new DateTime(2026, 9, 22, 18, 42, 0);
            for (int i = 0; i < count; i++)
                _sample.Add(new RunRecord
                {
                    date = t.AddDays(-3 * (count - 1 - i)).AddMinutes(-47 * i).ToString("s", CultureInfo.InvariantCulture),
                    seed = 1000 + i,
                    // One sample keeps the old look: a record written before the Pangäas had names.
                    name = i == 1 ? "" : IslandNames.ForSeed(1000 + i),
                    playSeconds = 540f + 83f * i,
                    islandsMerged = 16 + i % 5,
                    landArea = 820f + 137f * i,
                    speciesSeen = 9 + 2 * i,
                    photos = i % 4 * 3,
                    image = i == 2 ? "" : "sample",
                });
            _usingSample = true;
            _records = _sample;
            Show();
        }

        void Show()
        {
            _open = true;
            _canvas.enabled = true;
            _screen.gameObject.SetActive(true);
            _screen.SetAsLastSibling();
            _view.SetActive(false);
            _viewIndex = -1;
            _wide = -1;
            Layout();
            _content.anchoredPosition = Vector2.zero;
            _scroll.velocity = Vector2.zero;
            if (!Application.isPlaying) for (int i = 0; i < 12 && LoadNext(); i++) { }
        }

        public void Close()
        {
            bool was = _open;
            _open = false;
            _dirty = false;
            ReleaseView();
            foreach (var c in _cards) ReleaseCard(c);
            if (_view != null) _view.SetActive(false);
            if (_screen != null) _screen.gameObject.SetActive(false);
            if (_canvas != null) _canvas.enabled = false;
            _records = null;
            _viewIndex = -1;
            if (was) s_closedFrame = Time.frameCount;
        }

        // Escape / Android back: full picture -> list -> closed.
        public void Back()
        {
            if (ViewingPicture) HidePicture();
            else Close();
        }

        public void Tick()
        {
            if (!_open) return;
            if (ViewingPicture)
            {
                if (_root.rect.size != _viewFittedFor) FitPicture();
                return;
            }
            LoadNext();
        }

        // ---------------------------------------------------------------- list

        void Refill()
        {
            _dirty = false;
            if (!_usingSample) _records = RunJournal.Records;
            int n = _records != null ? _records.Count : 0;
            while (_cards.Count < n) _cards.Add(BuildCard(_content, _cards.Count));
            _count.text = n == 0 ? "Deine vollendeten Welten" : n == 1 ? "1 vollendete Welt" : n + " vollendete Welten";
            SetActive(_empty.gameObject, n == 0);
            SetActive(_list.gameObject, n > 0);

            float w = CardWidth();
            int rows = (n + _columns - 1) / _columns;
            _content.sizeDelta = new Vector2(0f, Mathf.Max(0f, rows * (CardH + CardGap) - CardGap));
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                bool on = i < n;
                SetActive(c.root.gameObject, on);
                if (!on)
                {
                    ReleaseCard(c);
                    continue;
                }
                // Newest first; the number stays the chronological one.
                int record = n - 1 - i;
                if (c.record != record) ReleaseCard(c);
                c.record = record;
                c.root.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i % _columns * (w + CardGap), -(i / _columns) * (CardH + CardGap)), new Vector2(w, CardH));
                FillCard(c, _records[record], record + 1);
            }
        }

        float CardWidth()
        {
            float listW = (_wide == 1 ? WideSize.x : TallSize.x) - 2f * 50f;
            return (listW - (_columns - 1) * CardGap) / _columns;
        }

        void FillCard(CardView c, RunRecord r, int number)
        {
            c.title.text = RunJournal.TitleOf(r, number);
            c.date.text = CardSubtitle(r, number);
            c.values[0].text = FormatPlayTime(r.playSeconds);
            c.values[1].text = r.islandsMerged.ToString();
            c.values[2].text = Mathf.RoundToInt(r.landArea).ToString("N0", GermanNumbers);
            c.values[3].text = r.speciesSeen.ToString();
            c.values[4].text = r.photos.ToString();
            if (c.texture == null)
            {
                c.picture.texture = null;
                c.picture.color = UiStyle.WithAlpha(UiStyle.Cream, 0f);
                c.placeholder.enabled = true;
            }
        }

        // One picture per call, for the cards around the visible part of the list; far-away ones are released.
        bool LoadNext()
        {
            if (_records == null || _records.Count == 0) return false;
            float step = CardH + CardGap, top = Mathf.Max(0f, _content.anchoredPosition.y), height = _list.rect.height;
            int firstRow = Mathf.FloorToInt(top / step), lastRow = Mathf.FloorToInt((top + Mathf.Max(height, step)) / step);
            int n = _records.Count;
            for (int i = 0; i < n && i < _cards.Count; i++)
            {
                int row = i / _columns;
                var c = _cards[i];
                if ((row < firstRow - KeepAhead || row > lastRow + KeepAhead) && c.tried) ReleaseCard(c);
            }
            int from = Mathf.Max(0, (firstRow - LoadAhead) * _columns), to = Mathf.Min(n, (lastRow + LoadAhead + 1) * _columns);
            for (int i = from; i < to && i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (c.tried || c.record < 0) continue;
                c.tried = true;
                c.texture = LoadPicture(c.record);
                if (c.texture != null)
                {
                    c.picture.texture = c.texture;
                    c.picture.uvRect = CoverCrop(c.texture.width, c.texture.height, PicW / PicH);
                    c.picture.color = Color.white;
                    c.placeholder.enabled = false;
                }
                return true;
            }
            return false;
        }

        Texture2D LoadPicture(int record)
        {
            if (_records == null || record < 0 || record >= _records.Count) return null;
            var r = _records[record];
            if (_usingSample) return string.IsNullOrEmpty(r.image) ? null : SamplePicture(record, 320, 180);
            return RunJournal.LoadPicture(r);
        }

        static void ReleaseCard(CardView c)
        {
            if (c.picture != null)
            {
                c.picture.texture = null;
                c.picture.color = UiStyle.WithAlpha(UiStyle.Cream, 0f);
            }
            if (c.placeholder != null) c.placeholder.enabled = true;
            Dispose(c.texture);
            c.texture = null;
            c.tried = false;
            c.record = -1;
        }

        // ---------------------------------------------------------------- full picture

        public void ShowPicture(int cardIndex)
        {
            if (_records == null || cardIndex < 0 || cardIndex >= _records.Count) return;
            int record = _records.Count - 1 - cardIndex;
            ReleaseView();
            _viewIndex = record;
            var r = _records[record];
            _viewTexture = LoadPicture(record);
            _viewPicture.texture = _viewTexture;
            _viewPicture.color = _viewTexture != null ? Color.white : UiStyle.WithAlpha(UiStyle.Cream, 0f);
            _viewTitle.text = RunJournal.TitleOf(r, record + 1);
            _viewDate.text = CardSubtitle(r, record + 1);
            _viewStats.text = StatsLine(r);
            // The list steps aside like the photo album's grid; its scroll position stays.
            SetActive(_screen.gameObject, false);
            _view.SetActive(true);
            _view.transform.SetAsLastSibling();
            FitPicture();
        }

        public void HidePicture()
        {
            ReleaseView();
            _viewIndex = -1;
            if (_view != null) _view.SetActive(false);
            if (_open && _screen != null) SetActive(_screen.gameObject, true);
        }

        void ReleaseView()
        {
            if (_viewPicture != null) _viewPicture.texture = null;
            Dispose(_viewTexture);
            _viewTexture = null;
        }

        // The picture fills the room between the title lines and the stats line at its own aspect.
        void FitPicture()
        {
            _viewFittedFor = _root.rect.size;
            Vector2 room = new Vector2(Mathf.Max(200f, _viewFittedFor.x - 2f * UiStyle.Margin), Mathf.Max(200f, _viewFittedFor.y - 560f));
            float aspect = _viewTexture != null && _viewTexture.height > 0 ? _viewTexture.width / (float)_viewTexture.height : 16f / 9f;
            Vector2 size = room.x / room.y > aspect ? new Vector2(room.y * aspect, room.y) : new Vector2(room.x, room.x / aspect);
            _viewFrame.Center(new Vector2(0f, 10f), size);
        }

        // Under the name: the chronological number and the date. A record from before the names carries its number
        // as its title already, so the line is only the date there.
        public static string CardSubtitle(RunRecord r, int number)
        {
            string date = FormatDate(r != null ? r.date : null);
            if (r == null || string.IsNullOrEmpty(r.name)) return date;
            string head = "Pangäa #" + Mathf.Max(1, number);
            return string.IsNullOrEmpty(date) ? head : head + "  ·  " + date;
        }

        public static string StatsLine(RunRecord r) =>
            FormatPlayTime(r.playSeconds) + "  ·  " + r.islandsMerged + " Inseln  ·  " + r.speciesSeen + " Arten  ·  " + r.photos + " Fotos";

        // ---------------------------------------------------------------- formatting

        public static string FormatPlayTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            if (s < 60) return s + " Sek.";
            int minutes = Mathf.RoundToInt(s / 60f);
            if (minutes < 60) return minutes + " Min.";
            return minutes / 60 + " Std. " + (minutes % 60).ToString("00") + " Min.";
        }

        public static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return iso;
            return t.Day + ". " + Months[t.Month - 1] + " " + t.Year + ", " + t.ToString("HH:mm", CultureInfo.InvariantCulture) + " Uhr";
        }

        // uvRect that fills a frame of the given aspect with the middle of the picture.
        public static Rect CoverCrop(int width, int height, float frameAspect)
        {
            if (width <= 0 || height <= 0 || frameAspect <= 0f) return new Rect(0f, 0f, 1f, 1f);
            float aspect = width / (float)height;
            if (aspect > frameAspect)
            {
                float w = frameAspect / aspect;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = aspect / frameAspect;
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _cards.Clear();
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, out _root);

            _screen = UiStyle.Scrim(_root, "RunJournalScreen", UiStyle.Dim);
            // The fade-in animates the panel's own scale, so the fit-to-screen scale sits one level above it.
            _fit = UiStyle.Rect(_screen, "Fit").Center(Vector2.zero, Vector2.zero);
            _panel = UiStyle.Panel(_fit, "Panel", TallSize, true).Center(Vector2.zero, TallSize);
            UiStyle.FadeIn(_screen.gameObject, _panel);

            _title = UiStyle.FitWidth(UiStyle.Label(_panel, "Durchgangs-Tagebuch", 80, UiStyle.Sand, TextAnchor.MiddleCenter, true));
            _title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -85f), new Vector2(860f, 110f));
            _count = UiStyle.FitWidth(UiStyle.Label(_panel, "", 34, UiStyle.CreamSoft, TextAnchor.MiddleCenter));
            _count.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -162f), new Vector2(860f, 44f));

            // The list: a clipped viewport that scrolls by drag, wheel and inertia; the transparent body makes the
            // gaps between cards draggable too.
            var viewport = UiStyle.Shape(_panel, "List", null, UiStyle.WithAlpha(Color.white, 0f), true);
            _list = viewport.rectTransform;
            _list.gameObject.AddComponent<RectMask2D>();
            _content = UiStyle.Rect(_list, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            _scroll = _list.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _list;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.12f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;
            _scroll.scrollSensitivity = 60f;

            _empty = UiStyle.Rect(_panel, "Empty");
            var globe = UiStyle.Dot(_empty, "Globe", 200f, UiStyle.WithAlpha(UiStyle.Sky, 0.22f));
            globe.rectTransform.TopCenter(new Vector2(0f, -10f), new Vector2(200f, 200f));
            UiStyle.Shape(globe.transform, "Ring", UiSprites.CircleRing, UiStyle.Faint).rectTransform.Stretch();
            var line1 = UiStyle.Label(_empty, "Noch kein Pangäa vollendet", UiStyle.Subheading, UiStyle.Cream, TextAnchor.UpperCenter, true);
            line1.rectTransform.TopCenter(new Vector2(0f, -240f), new Vector2(780f, 60f));
            var line2 = UiStyle.Label(_empty, "Vereine alle Inseln einer Welt zu einem einzigen Kontinent. Jedes vollendete Pangäa bekommt hier eine Seite mit seinem Bild aus dem All.", UiStyle.Body, UiStyle.Muted);
            line2.rectTransform.TopCenter(new Vector2(0f, -316f), new Vector2(740f, 220f));

            _close = UiStyle.PrimaryButton(_panel, "CloseRunJournal", "Zurück", new Vector2(680f, 124f), Close);
            ((RectTransform)_close.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(680f, 124f));
            UiStyle.ButtonIcon(_close, UiIcon.Back);

            BuildView(_root);

            _screen.gameObject.SetActive(false);
            _view.SetActive(false);
            _canvas.enabled = false;
            _open = false;
            _wide = -1;
            UiStyle.OnResize(_screen, Layout);
        }

        CardView BuildCard(RectTransform parent, int i)
        {
            var c = new CardView { root = UiStyle.Card(parent, "Run" + i, new Vector2(860f, CardH)) };
            var body = c.root.GetComponent<Image>();
            body.raycastTarget = true;
            var button = c.root.gameObject.AddComponent<Button>();
            button.targetGraphic = body;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            int slot = i;
            button.onClick.AddListener(() => ShowPicture(slot));
            c.root.gameObject.AddComponent<UiPressFeedback>().pressedScale = 0.97f;

            c.picture = UiStyle.Picture(c.root, "Picture", new Vector2(PicW, PicH), out var frame);
            frame.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(CardPad, 0f), new Vector2(PicW, PicH));
            c.picture.color = UiStyle.WithAlpha(UiStyle.Cream, 0f);
            // Stands in for the planet until the picture is loaded (or when a run has none).
            c.placeholder = UiStyle.Dot(frame, "Planet", 150f, UiStyle.WithAlpha(UiStyle.Sky, 0.2f));
            c.placeholder.rectTransform.Center(Vector2.zero, new Vector2(150f, 150f));

            float x = CardPad + PicW + 26f;
            c.title = UiStyle.FitWidth(UiStyle.Label(c.root, "", 44, UiStyle.Sand, TextAnchor.MiddleLeft, true));
            Row(c.title.rectTransform, x, CardPad, 18f, 54f, 0f);
            c.date = UiStyle.FitWidth(UiStyle.Label(c.root, "", 25, UiStyle.Muted, TextAnchor.MiddleLeft));
            Row(c.date.rectTransform, x, CardPad, 74f, 34f, 0f);
            var line = UiStyle.Shape(c.root, "Divider", UiSprites.PillOf(4f), UiStyle.Line);
            Row(line.rectTransform, x, CardPad, 118f, 3f, 0.5f);
            for (int k = 0; k < StatCount; k++)
            {
                float top = 128f + k * 33f;
                var name = UiStyle.FitWidth(UiStyle.Label(c.root, StatNames[k], 26, UiStyle.CreamSoft, TextAnchor.MiddleLeft));
                Row(name.rectTransform, x, CardPad + 110f, top, 33f, 0f);
                c.values[k] = UiStyle.FitWidth(UiStyle.Label(c.root, "", 26, UiStyle.Cream, TextAnchor.MiddleRight, true));
                Row(c.values[k].rectTransform, x + 100f, CardPad, top, 33f, 1f);
            }
            return c;
        }

        void BuildView(RectTransform root)
        {
            var scrim = UiStyle.Scrim(root, "RunJournalPicture", UiStyle.WithAlpha(UiStyle.Dim, 0.92f));
            _view = scrim.gameObject;
            UiStyle.FadeIn(_view, null);

            _viewTitle = UiStyle.Label(scrim, "", UiStyle.Heading, UiStyle.Sand, TextAnchor.MiddleCenter, true);
            _viewTitle.rectTransform.TopCenter(new Vector2(0f, -50f), new Vector2(1000f, 76f));
            _viewDate = UiStyle.FitWidth(UiStyle.Label(scrim, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.MiddleCenter));
            _viewDate.rectTransform.TopCenter(new Vector2(0f, -132f), new Vector2(1000f, 40f));

            _viewPicture = UiStyle.Picture(scrim, "Picture", new Vector2(960f, 540f), out _viewFrame);
            _viewFrame.Center(Vector2.zero, new Vector2(960f, 540f));

            _viewStats = UiStyle.FitWidth(UiStyle.Label(scrim, "", UiStyle.Body, UiStyle.CreamSoft, TextAnchor.MiddleCenter));
            _viewStats.rectTransform.BottomCenter(new Vector2(0f, 206f), new Vector2(1000f, 50f));

            _viewBack = UiStyle.PrimaryButton(scrim, "BackToRunJournal", "Zurück", new Vector2(680f, 124f), HidePicture);
            ((RectTransform)_viewBack.transform).BottomCenter(new Vector2(0f, 60f), new Vector2(680f, 124f));
            UiStyle.ButtonIcon(_viewBack, UiIcon.Back);
        }

        // ---------------------------------------------------------------- layout

        // Portrait: one column of cards. Landscape: two columns in a wide panel. Runs from the resize callback too.
        void Layout()
        {
            if (_screen == null || _panel == null) return;
            var screen = _screen.rect;
            int wide = screen.width >= WideFrom ? 1 : 0;
            var size = wide == 1 ? WideSize : TallSize;
            float fit = Mathf.Clamp(Mathf.Min((screen.width - 2f * UiStyle.Margin) / size.x, (screen.height - 2f * UiStyle.Margin) / size.y), 0.5f, MaxFit);
            if (screen.width > 1f) _fit.localScale = new Vector3(fit, fit, 1f);
            if (wide == _wide) return;
            _wide = wide;
            _columns = wide == 1 ? 2 : 1;
            _panel.sizeDelta = size;
            _title.rectTransform.sizeDelta = new Vector2(size.x - 100f, 110f);
            _count.rectTransform.sizeDelta = new Vector2(size.x - 100f, 44f);
            _list.Stretch(50f, FooterH, 50f, HeaderH);
            _empty.Stretch(50f, FooterH, 50f, HeaderH + 20f);
            if (_open) Refill();
        }

        // ---------------------------------------------------------------- helpers

        // A strip across the card, top offset and height fixed; the pivot is where a FitWidth label shrinks to.
        static void Row(RectTransform rt, float left, float right, float top, float height, float pivotX)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(pivotX, 0.5f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        static void Dispose(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void ReleaseSample()
        {
            _sample.Clear();
            _usingSample = false;
        }

        // A made-up view from space for the Edit Mode preview: stars, a blue planet and a green continent.
        static Texture2D SamplePicture(int index, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var rng = new System.Random(1234 + index);
            Color space = new Color(0.03f, 0.05f, 0.12f), sea = Color.HSVToRGB(Mathf.Repeat(0.56f + index * 0.02f, 1f), 0.6f, 0.75f);
            Color land = Color.HSVToRGB(0.28f + 0.03f * (index % 3), 0.5f, 0.62f), sand = new Color(0.93f, 0.85f, 0.62f);
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f), blob = new Vector2(0.15f * (index % 3 - 1), 0.1f);
            float r = h * 0.42f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Vector2 p = new Vector2(x, y) - c;
                    float d = p.magnitude / r;
                    Color col = space;
                    if (d < 1f)
                    {
                        Vector2 u = p / r;
                        float l = (u - blob).magnitude + 0.08f * Mathf.Sin(u.x * 11f + index) * Mathf.Cos(u.y * 9f);
                        col = l < 0.42f ? land : l < 0.47f ? sand : sea;
                        col *= Mathf.Lerp(1.05f, 0.55f, Mathf.Clamp01(d * d + 0.3f * (u.x + u.y)));
                    }
                    else if (d < 1.06f) col = Color.Lerp(new Color(0.5f, 0.75f, 1f), space, (d - 1f) / 0.06f);
                    else if (rng.NextDouble() < 0.004) col = Color.white * 0.8f;
                    px[y * w + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
