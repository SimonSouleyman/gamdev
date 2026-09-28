using System;
using System.Text;
using Drift.Core;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // What the "Insulaner" tab shows about the player's island; gathered by WatchTools, read-only.
    public struct JournalIslandInfo
    {
        public string biome, stage, buildingLine;
        public int animals, herds, plants, trees, absorbed, foreignKinds, buildings, settlers, villages;
    }

    // The collection album: a builder/controller like PhotoAlbum and HelpScreen that WatchTools places into its
    // canvas. Tabs per biome plus "Meer & Himmel" and "Insulaner", a pool of soft cards that is refilled per tab
    // and page, and two layouts (a portrait column, a landscape two-pane) switched by the screen rect.
    public sealed class JournalPanel
    {
        public const int TabCount = 6;
        public const int IslandTab = 5;
        // Not a tab button of its own: the "Fotoaufgaben" button in the header opens it.
        public const int TasksTab = 6;
        public const float WideFrom = 2100f;
        const int Pool = 15, MaxPages = 4;
        const float CardW = 424f, CardH = 128f, CardGap = 12f, TabH = 76f, MaxFit = 1.5f;
        // Card inside: swatch on the left, text column, and on the right the photo slot (a big tap target of its own).
        public const float SwatchSize = 76f, SwatchX = 52f, TextX = 102f, SlotSize = 100f, SlotHitW = 112f, SlotRight = 6f;
        const float ChipW = 116f, WhenGap = 8f;
        public static float TextWidth(bool slot) => (slot ? CardW - SlotRight - SlotHitW - 4f : CardW - 14f) - TextX;
        static readonly Vector2 TallSize = new Vector2(960f, 1500f), WideSize = new Vector2(1980f, 1080f);

        static readonly string[] TabNames = { "Gemäßigt", "Tropisch", "Nordisch", "Savanne", "Meer & Himmel", "Insulaner" };
        static readonly string[] SectionTitles = { "Gemäßigte Inseln", "Tropische Inseln", "Nordische Inseln", "Savanneninseln", "Meer & Himmel", "Deine Insel und ihre Bewohner" };
        static readonly string[] BuildingNames =
        {
            "Lagerfeuer", "Zelt", "Hütte", "Brunnen", "Garten", "Steg", "Haus", "Halle", "Windmühle", "Leuchtturm", "Steinhaus", "Markt", "Feld", "Schrein",
        };

        sealed class CardView
        {
            public RectTransform root;
            public Image body, swatch, glyph, chip;
            public GameObject badge, go;
            public Text name, state, when, detail;
            public Button button;
            public int entry = -1;
            // Photo tasks: the big picture over the swatch (tasks view) and the small photo slot of a species card.
            public RawImage thumb, slotPicture;
            public GameObject thumbFrame, slot, slotRing, slotPictureFrame, slotDone, slotCheck;
            public Button slotButton;
            public int task = -1;
        }

        RectTransform _screen, _fit, _panel, _pager, _islandA, _islandB;
        Text _title, _count, _book, _sectionTitle, _sectionCount, _islandAText, _islandBText;
        UiBar _bar, _sectionBar;
        UiPageDots _dots;
        Button _prev, _next, _close;
        readonly Button[] _tabs = new Button[TabCount];
        readonly Text[] _tabName = new Text[TabCount], _tabCount = new Text[TabCount];
        readonly Image[] _tabLip = new Image[TabCount];
        readonly CardView[] _cards = new CardView[Pool];
        readonly StringBuilder _sb = new StringBuilder(256);
        DiscoveryJournal _journal;
        LifeBook _bookRef;
        PhotoTaskBook _tasks;
        Func<int, Texture> _thumbOf;
        Button _tasksButton, _resetButton;
        Text _tasksName, _tasksCount;
        Image _tasksLip;
        JournalIslandInfo _info;
        int _wide = -1, _tab, _page, _columns = 2, _capacity = 10;
        Vector2 _gridOrigin;

        public GameObject Root => _screen != null ? _screen.gameObject : null;
        public bool Wide => _wide == 1;
        public int Tab => _tab;
        public int Page => _page;
        public int Capacity => _capacity;
        public int PageCount => PagesOf(_tab, _capacity);

        public static string BuildingName(BuildingKind kind) => BuildingNames[Mathf.Clamp((int)kind, 0, BuildingNames.Length - 1)];

        public static int PagesOf(int tab, int capacity) =>
            tab == TasksTab ? Mathf.Max(1, (PhotoTaskCatalog.Count + capacity - 1) / Mathf.Max(1, capacity))
            : tab >= IslandTab ? 1 : Mathf.Max(1, (CollectionCatalog.CountIn((CollectSection)tab) + capacity - 1) / Mathf.Max(1, capacity));

        public PhotoTaskBook PhotoTasks => _tasks;

        // The photo tasks shown in the header button, the tasks view and on the species cards; thumbOf gives the
        // picture of a done task (null while there is none). A null book hides all of it.
        public void SetPhotoTasks(PhotoTaskBook tasks, Func<int, Texture> thumbOf)
        {
            _tasks = tasks;
            _thumbOf = thumbOf;
            if (_tasks == null && _tab == TasksTab) _tab = 0;
            Refill();
        }

        // Taps the card of a catalog entry on the page that is up; false when it is not there (or not tappable).
        // The real button handler runs, so tests and eval take the same route as a finger.
        public bool TapCard(int entry)
        {
            for (int i = 0; i < Pool; i++)
            {
                var c = _cards[i];
                if (c == null || c.entry != entry || c.go == null || !c.go.activeSelf || c.button == null || !c.button.interactable) continue;
                c.button.onClick.Invoke();
                return true;
            }
            return false;
        }

        // Taps the photo slot of a catalog entry's card (the real handler, like TapCard); false when it is not shown.
        public bool TapSlot(int entry)
        {
            for (int i = 0; i < Pool; i++)
            {
                var c = _cards[i];
                if (c == null || c.entry != entry || !c.root.gameObject.activeInHierarchy || c.slot == null || !c.slot.activeSelf) continue;
                c.slotButton.onClick.Invoke();
                return true;
            }
            return false;
        }

        // Screen rects of a card and of its photo slot (for tests of the layout: the slot must not overlap the texts).
        public bool CardRects(int entry, out RectTransform card, out RectTransform slot, out Text name, out Text detail, out Text when)
        {
            for (int i = 0; i < Pool; i++)
            {
                var c = _cards[i];
                if (c == null || c.entry != entry || !c.root.gameObject.activeInHierarchy) continue;
                card = c.root;
                slot = (RectTransform)c.slot.transform;
                name = c.name;
                detail = c.detail;
                when = c.when;
                return true;
            }
            card = slot = null;
            name = detail = when = null;
            return false;
        }

        // ---------------------------------------------------------------- build

        Action<int> _onWatch;
        Action<JournalResetScope> _onReset;

        // onWatch gets the catalog index of a card the player tapped (only cards of seen or collected entries);
        // onReset runs "Tagebuch zurücksetzen" once the player confirmed a scope (null hides the button).
        public GameObject Build(RectTransform root, Action onClose, Action<int> onWatch = null, Action<JournalResetScope> onReset = null)
        {
            _onWatch = onWatch;
            _onReset = onReset;
            _screen = UiStyle.Scrim(root, "JournalScreen", UiStyle.Dim);
            // The fade-in animates the panel's own scale, so the fit-to-screen scale sits one level above it.
            _fit = UiStyle.Rect(_screen, "Fit").Center(Vector2.zero, Vector2.zero);
            _panel = UiStyle.Panel(_fit, "Panel", new Vector2(960f, 1500f), true).Center(Vector2.zero, new Vector2(960f, 1500f));
            UiStyle.FadeIn(_screen.gameObject, _panel);

            _title = UiStyle.Label(_panel, "Tagebuch", 84, UiStyle.Sand, TextAnchor.MiddleCenter, true);
            _count = UiStyle.FitWidth(UiStyle.Label(_panel, "", 34, UiStyle.CreamSoft, TextAnchor.MiddleCenter));
            _bar = UiStyle.Bar(_panel, "Progress", new Vector2(860f, 22f), UiStyle.Mint);
            _book = UiStyle.FitWidth(UiStyle.Label(_panel, "", 26, UiStyle.Muted, TextAnchor.MiddleCenter));

            for (int i = 0; i < TabCount; i++)
            {
                int tab = i;
                var b = UiStyle.SecondaryButton(_panel, "Tab" + i, TabNames[i], new Vector2(278f, TabH), () => SetTab(tab));
                _tabs[i] = b;
                _tabName[i] = UiStyle.LabelOf(b);
                _tabName[i].fontSize = 30;
                _tabName[i].rectTransform.Stretch(16f, 28f, 16f, 5f);
                _tabCount[i] = UiStyle.Label(b.transform, "", 22, UiStyle.Muted, TextAnchor.MiddleCenter);
                _tabCount[i].rectTransform.Stretch(16f, 7f, 16f, 46f);
                var lip = b.transform.Find("Lip");
                _tabLip[i] = lip != null ? lip.GetComponent<Image>() : null;
            }

            _tasksButton = UiStyle.SecondaryButton(_panel, "PhotoTasks", "Fotoaufgaben", new Vector2(240f, TabH), () => SetTab(TasksTab));
            _tasksName = UiStyle.LabelOf(_tasksButton);
            _tasksName.fontSize = 28;
            _tasksName.rectTransform.Stretch(16f, 28f, 16f, 5f);
            UiStyle.FitWidth(_tasksName);
            _tasksCount = UiStyle.Label(_tasksButton.transform, "", 22, UiStyle.Muted, TextAnchor.MiddleCenter);
            _tasksCount.rectTransform.Stretch(16f, 7f, 16f, 46f);
            var tasksLip = _tasksButton.transform.Find("Lip");
            _tasksLip = tasksLip != null ? tasksLip.GetComponent<Image>() : null;

            _sectionTitle = UiStyle.FitWidth(UiStyle.Label(_panel, "", 34, UiStyle.Sand, TextAnchor.MiddleLeft, true));
            _sectionCount = UiStyle.FitWidth(UiStyle.Label(_panel, "", 28, UiStyle.CreamSoft, TextAnchor.MiddleRight));
            _sectionBar = UiStyle.Bar(_panel, "SectionProgress", new Vector2(860f, 16f), UiStyle.Sky);

            for (int i = 0; i < Pool; i++)
            {
                var card = BuildCard(_panel, i);
                _cards[i] = card;
                card.button.onClick.AddListener(() =>
                {
                    if (card.entry >= 0) _onWatch?.Invoke(card.entry);
                });
                card.slotButton.onClick.AddListener(() =>
                {
                    if (card.task >= 0) ShowTaskDetail(card.task);
                });
            }

            _islandA = UiStyle.Card(_panel, "Islanders", new Vector2(860f, 330f));
            _islandAText = IslandCard(_islandA, "Insulaner");
            _islandB = UiStyle.Card(_panel, "IslandStats", new Vector2(860f, 330f));
            _islandBText = IslandCard(_islandB, "Deine Insel");

            _pager = UiStyle.Rect(_panel, "Pager");
            _pager.sizeDelta = new Vector2(420f, 72f);
            _prev = UiStyle.IconButton(_pager, "Prev", 68f, UiIcon.Back, PrevPage);
            ((RectTransform)_prev.transform).Center(new Vector2(-170f, 0f), new Vector2(68f, 68f));
            _next = UiStyle.IconButton(_pager, "Next", 68f, UiIcon.Back, NextPage);
            ((RectTransform)_next.transform).Center(new Vector2(170f, 0f), new Vector2(68f, 68f));
            foreach (var n in new[] { "Icon", "IconShade" })
            {
                var icon = _next.transform.Find(n);
                if (icon != null) icon.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }
            _dots = UiStyle.PageDots(_pager, "Dots", MaxPages);
            _dots.Root.Center(Vector2.zero, _dots.Root.sizeDelta);

            _close = UiStyle.PrimaryButton(_panel, "Close", "Schließen", new Vector2(680f, 124f), onClose);

            // A quiet ghost pill on the island page only; it opens a confirmation, never resets by itself.
            _resetButton = UiStyle.SecondaryButton(_panel, "ResetJournal", "Tagebuch zurücksetzen", new Vector2(440f, 72f), OpenResetConfirm);
            _resetButton.targetGraphic.color = UiStyle.WithAlpha(UiStyle.Rose, 0.3f);
            var resetLabel = UiStyle.LabelOf(_resetButton);
            resetLabel.fontSize = 28;
            resetLabel.color = UiStyle.CreamSoft;

            BuildOverlay();

            _wide = -1;
            Layout();
            UiStyle.OnResize(_screen, Layout);
            return _screen.gameObject;
        }

        static Text IslandCard(RectTransform card, string title)
        {
            var head = UiStyle.Label(card, title, 36, UiStyle.Sand, TextAnchor.UpperCenter, true);
            head.rectTransform.Stretch(UiStyle.Gap, 0f, UiStyle.Gap, 18f);
            var body = UiStyle.Label(card, "", 29, UiStyle.Cream, TextAnchor.UpperCenter);
            body.rectTransform.Stretch(UiStyle.Gap, UiStyle.GapSmall, UiStyle.Gap, 74f);
            body.lineSpacing = UiStyle.Lines(1.2f);
            return body;
        }

        static CardView BuildCard(RectTransform parent, int i)
        {
            var c = new CardView { root = UiStyle.Card(parent, "Card" + i, new Vector2(CardW, CardH)) };
            c.body = c.root.GetComponent<Image>();
            c.swatch = UiStyle.Dot(c.root, "Swatch", SwatchSize, Color.white);
            c.swatch.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(SwatchX, 0f), new Vector2(SwatchSize, SwatchSize));
            UiStyle.Shape(c.swatch.transform, "Shade", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Shadow, 0.35f)).rectTransform.Stretch();
            c.glyph = UiStyle.Shape(c.swatch.transform, "Glyph", null, UiStyle.Ink);
            c.glyph.rectTransform.Center(Vector2.zero, new Vector2(52f, 52f));
            var badge = UiStyle.Dot(c.root, "Badge", 34f, UiStyle.Mint);
            badge.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(SwatchX + 30f, -30f), new Vector2(34f, 34f));
            UiStyle.Shape(badge.transform, "Check", JournalGlyphs.Of(JournalGlyph.Check), UiStyle.Ink).rectTransform.Center(Vector2.zero, new Vector2(24f, 24f));
            c.badge = badge.gameObject;

            c.name = UiStyle.FitWidth(UiStyle.Label(c.root, "", 30, UiStyle.Cream, TextAnchor.MiddleLeft, true));
            c.name.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(TextX, -30f), new Vector2(TextWidth(false), 42f));

            // The whole card is the button: tapping it watches the species. No icon for it any more (two camera
            // symbols on one card read as two photo buttons); the photo slot on the right is a button of its own.
            c.body.raycastTarget = true;
            c.button = c.root.gameObject.AddComponent<Button>();
            c.button.targetGraphic = c.body;
            c.button.transition = Selectable.Transition.None;
            c.button.navigation = new Navigation { mode = Navigation.Mode.None };
            c.root.gameObject.AddComponent<UiPressFeedback>().pressedScale = 0.96f;
            c.go = c.root.gameObject;
            c.chip = UiStyle.Pill(c.root, "State", new Vector2(ChipW, 30f), UiStyle.Ghost);
            c.chip.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(TextX, -68f), new Vector2(ChipW, 30f));
            c.state = UiStyle.FitWidth(UiStyle.Label(c.chip.transform, "", 20, UiStyle.Cream, TextAnchor.MiddleCenter, true));
            c.state.rectTransform.Stretch(6f, 0f, 6f, 2f);
            c.when = UiStyle.FitWidth(UiStyle.Label(c.root, "", 21, UiStyle.Muted, TextAnchor.MiddleLeft));
            c.when.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(TextX + ChipW + WhenGap, -68f), new Vector2(TextWidth(false) - ChipW - WhenGap, 30f));
            c.detail = UiStyle.FitWidth(UiStyle.Label(c.root, "", 22, UiStyle.CreamSoft, TextAnchor.MiddleLeft), 0.5f);
            c.detail.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(TextX, -103f), new Vector2(TextWidth(false), 30f));

            c.thumb = UiStyle.Picture(c.root, "TaskPhoto", new Vector2(SwatchSize + 6f, SwatchSize + 6f), out var frame);
            frame.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(SwatchX, 0f), new Vector2(SwatchSize + 6f, SwatchSize + 6f));
            frame.SetSiblingIndex(c.badge.transform.GetSiblingIndex());
            c.thumbFrame = frame.gameObject;
            c.thumbFrame.SetActive(false);

            // The photo slot: the open task (ring and camera) or its photo, at the right edge. Its hit area is the full
            // card height, and it answers taps itself, so they never reach the card's watch button.
            var slot = UiStyle.Rect(c.root, "PhotoSlot");
            slot.Place(new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-SlotRight - SlotHitW * 0.5f, 0f), new Vector2(SlotHitW, CardH));
            var hit = UiStyle.Shape(slot, "Hit", null, new Color(1f, 1f, 1f, 0f), true);
            hit.rectTransform.Stretch();
            c.slotButton = slot.gameObject.AddComponent<Button>();
            c.slotButton.targetGraphic = hit;
            c.slotButton.transition = Selectable.Transition.None;
            c.slotButton.navigation = new Navigation { mode = Navigation.Mode.None };
            slot.gameObject.AddComponent<UiPressFeedback>().pressedScale = 0.92f;
            var look = UiStyle.Rect(slot, "Look");
            look.Center(Vector2.zero, new Vector2(SlotSize, SlotSize));
            c.slot = slot.gameObject;
            var ring = UiStyle.Shape(look, "Empty", UiSprites.Rounded, UiStyle.WithAlpha(UiStyle.Sand, 0.12f));
            ring.rectTransform.Stretch();
            UiStyle.Shape(ring.transform, "Rim", UiSprites.Ring, UiStyle.WithAlpha(UiStyle.Sand, 0.55f)).rectTransform.Stretch();
            UiStyle.Icon(ring.transform, "Icon", UiIcon.Camera, 46f, UiStyle.WithAlpha(UiStyle.Sand, 0.85f)).rectTransform.Center(new Vector2(0f, 10f), new Vector2(46f, 46f));
            var word = UiStyle.Label(ring.transform, "Aufgabe", 17, UiStyle.WithAlpha(UiStyle.Sand, 0.85f), TextAnchor.MiddleCenter, true);
            word.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(SlotSize - 20f, 22f));
            UiStyle.FitWidth(word);
            c.slotRing = ring.gameObject;
            c.slotPicture = UiStyle.Picture(look, "Photo", new Vector2(SlotSize, SlotSize), out var slotFrame);
            slotFrame.Stretch();
            c.slotPictureFrame = slotFrame.gameObject;
            var done = UiStyle.Shape(look, "Done", UiSprites.Rounded, UiStyle.Mint);
            done.rectTransform.Stretch();
            UiStyle.Icon(done.transform, "Icon", UiIcon.Camera, 48f, UiStyle.Ink).rectTransform.Center(Vector2.zero, new Vector2(48f, 48f));
            c.slotDone = done.gameObject;
            var check = UiStyle.Dot(look, "Check", 32f, UiStyle.Mint);
            check.rectTransform.Place(new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-8f, -8f), new Vector2(32f, 32f));
            UiStyle.Shape(check.transform, "Mark", JournalGlyphs.Of(JournalGlyph.Check), UiStyle.Ink).rectTransform.Center(Vector2.zero, new Vector2(22f, 22f));
            c.slotCheck = check.gameObject;
            c.slot.SetActive(false);
            return c;
        }

        // ---------------------------------------------------------------- layout

        static void Put(RectTransform rt, float x, float y, float w, float h, float pivotX = 0.5f)
        {
            rt.Place(new Vector2(0f, 1f), new Vector2(pivotX, 0.5f), new Vector2(x + w * pivotX, -(y + h * 0.5f)), new Vector2(w, h));
        }

        // Portrait: one column (header, 2 x 3 tabs, 2 x 5 cards). Landscape: header and tabs on the left, 3 x 5 cards
        // on the right. Runs from the resize callback too, so an Edit Mode capture at another aspect is laid out right.
        void Layout()
        {
            if (_screen == null || _panel == null) return;
            var screen = _screen.rect;
            int wide = screen.width >= WideFrom ? 1 : 0;
            // A landscape canvas is 1920 units high like a portrait one, which leaves the panel small on a desktop:
            // it grows into the room it has, up to MaxFit.
            var size = wide == 1 ? WideSize : TallSize;
            float fit = Mathf.Clamp(Mathf.Min((screen.width - 2f * UiStyle.Margin) / size.x, (screen.height - 2f * UiStyle.Margin) / size.y), 0.5f, MaxFit);
            if (screen.width > 1f) _fit.localScale = new Vector3(fit, fit, 1f);
            if (wide == _wide) return;
            _wide = wide;
            const float m = 50f;
            if (wide == 1)
            {
                const float left = 540f, gridW = 3f * CardW + 2f * CardGap, gx = m + left + 44f;
                float H = WideSize.y;
                _panel.sizeDelta = WideSize;
                Put(_title.rectTransform, m, 22f, left, 110f);
                Put(_count.rectTransform, m, 134f, left, 44f);
                Put(_bar.Root, m, 190f, left, 22f);
                Put(_book.rectTransform, m, 222f, left, 36f);
                for (int i = 0; i < TabCount; i++) Put((RectTransform)_tabs[i].transform, m, 280f + i * (TabH + 14f), left, TabH);
                Put((RectTransform)_tasksButton.transform, m, 280f + TabCount * (TabH + 14f), left, TabH);
                Put((RectTransform)_close.transform, m, H - 36f - 124f, left, 124f);
                Put(_sectionTitle.rectTransform, gx, 40f, 620f, 44f, 0f);
                Put(_sectionCount.rectTransform, gx + gridW - 640f, 42f, 640f, 40f, 1f);
                Put(_sectionBar.Root, gx, 94f, gridW, 16f);
                _gridOrigin = new Vector2(gx, 130f);
                _columns = 3;
                Put(_pager, gx + gridW * 0.5f - 210f, 130f + 5f * (CardH + CardGap) + 4f, 420f, 72f);
                float iw = (gridW - CardGap) * 0.5f;
                Put(_islandA, gx, 130f, iw, 470f);
                Put(_islandB, gx + iw + CardGap, 130f, iw, 470f);
                Put((RectTransform)_resetButton.transform, gx + gridW * 0.5f - 220f, 130f + 470f + 28f, 440f, 72f);
            }
            else
            {
                float W = TallSize.x, H = TallSize.y, inner = W - 2f * m;
                _panel.sizeDelta = TallSize;
                // The title is centred in what the photo-task button leaves of the row.
                Put(_title.rectTransform, m, 22f, inner - 230f, 110f);
                Put((RectTransform)_tasksButton.transform, W - m - 220f, 40f, 220f, TabH);
                Put(_count.rectTransform, m, 134f, inner, 44f);
                Put(_bar.Root, m, 190f, inner, 22f);
                Put(_book.rectTransform, m, 222f, inner, 36f);
                float tw = (inner - 2f * CardGap) / 3f;
                for (int i = 0; i < TabCount; i++)
                    Put((RectTransform)_tabs[i].transform, m + (i % 3) * (tw + CardGap), 274f + (i / 3) * (TabH + 16f), tw, TabH);
                Put(_sectionTitle.rectTransform, m, 466f, 400f, 44f, 0f);
                Put(_sectionCount.rectTransform, m + inner - 450f, 468f, 450f, 40f, 1f);
                Put(_sectionBar.Root, m, 518f, inner, 16f);
                _gridOrigin = new Vector2(m, 552f);
                _columns = 2;
                Put(_pager, W * 0.5f - 210f, 552f + 5f * (CardH + CardGap) - 2f, 420f, 72f);
                Put((RectTransform)_close.transform, W * 0.5f - 340f, H - 36f - 124f, 680f, 124f);
                Put(_islandA, m, 552f, inner, 372f);
                Put(_islandB, m, 552f + 372f + CardGap, inner, 306f);
                Put((RectTransform)_resetButton.transform, W * 0.5f - 220f, 552f + 372f + CardGap + 306f + 12f, 440f, 72f);
            }
            _capacity = _columns * 5;
            for (int i = 0; i < Pool; i++)
                Put(_cards[i].root, _gridOrigin.x + (i % _columns) * (CardW + CardGap), _gridOrigin.y + (i / _columns) * (CardH + CardGap), CardW, CardH);
            Refill();
        }

        // ---------------------------------------------------------------- navigation

        public void SetTab(int tab, int page = 0)
        {
            CloseOverlay();
            _highlightTask = -1;
            _tab = Mathf.Clamp(tab, 0, _tasks != null ? TasksTab : TabCount - 1);
            _page = page;
            Refill();
        }

        public void NextPage()
        {
            _page++;
            Refill();
        }

        public void PrevPage()
        {
            _page--;
            Refill();
        }

        // ---------------------------------------------------------------- fill

        public void Fill(DiscoveryJournal journal, LifeBook book, in JournalIslandInfo info)
        {
            _journal = journal;
            _bookRef = book;
            _info = info;
            Layout();
            Refill();
        }

        // With the cross-run book the header counts everything ever found; this line is what the current run added.
        public static string RunLine(DiscoveryJournal j) =>
            "Diese Reise: " + j.RunSeenCount + " Arten gesehen  ·  " + (j.NewThisRun == 1 ? "1 neu entdeckt" : j.NewThisRun + " neu entdeckt");

        // "bei 6:42" / "seit 7:11" (run time) or, for a step of an earlier run, the day.
        public static string WhenOf(DiscoveryJournal j, int index)
        {
            var state = j.StateOf(index);
            if (state == CollectState.Unknown) return "";
            if (state == CollectState.Collected)
                return j.CollectedBefore(index) ? DayOf(j.CollectedOn(index)) : "seit " + DiscoveryJournal.FormatTime(j.CollectedAt(index));
            return j.SeenBefore(index) ? DayOf(j.SeenOn(index)) : "bei " + DiscoveryJournal.FormatTime(j.SeenAt(index));
        }

        static string DayOf(DateTime t) => t == DateTime.MinValue ? "früher" : "am " + t.ToString("dd.MM.", System.Globalization.CultureInfo.InvariantCulture);

        public static string ProgressLine(DiscoveryJournal j) =>
            "Gesammelt " + j.CollectedCount + "/" + CollectionCatalog.CollectibleCount + "  ·  Gesehen " + j.SeenCount + "/" + CollectionCatalog.Count
            + (j.Complete ? "  –  alles gefunden!" : "");

        void Refill()
        {
            var j = _journal;
            if (j == null || _screen == null) return;
            _count.text = ProgressLine(j);
            _bar.Set(j.CollectedCount / (float)CollectionCatalog.CollectibleCount);
            _book.text = j.HasBook ? RunLine(j) : _bookRef != null ? _bookRef.Line : "";

            for (int i = 0; i < TabCount; i++)
            {
                bool on = i == _tab;
                var body = _tabs[i].targetGraphic;
                if (body != null) body.color = on ? UiStyle.Mint : UiStyle.Lagoon;
                if (_tabLip[i] != null) _tabLip[i].color = on ? UiStyle.MintDeep : UiStyle.LagoonDeep;
                _tabName[i].color = on ? UiStyle.Ink : UiStyle.Cream;
                _tabCount[i].color = on ? UiStyle.WithAlpha(UiStyle.Ink, 0.75f) : UiStyle.Muted;
                _tabCount[i].text = i == IslandTab ? _info.stage ?? "" : TabProgress(j, (CollectSection)i);
            }
            SetActive(_tasksButton.gameObject, _tasks != null);
            if (_tasks != null)
            {
                bool on = _tab == TasksTab;
                var body = _tasksButton.targetGraphic;
                if (body != null) body.color = on ? UiStyle.Sand : UiStyle.Lagoon;
                if (_tasksLip != null) _tasksLip.color = on ? UiStyle.CreamDeep : UiStyle.LagoonDeep;
                _tasksName.color = on ? UiStyle.Ink : UiStyle.Cream;
                _tasksCount.color = on ? UiStyle.WithAlpha(UiStyle.Ink, 0.75f) : UiStyle.Muted;
                _tasksCount.text = _tasks.DoneCount + "/" + PhotoTaskBook.Total;
            }
            if (_tab == TasksTab && _tasks != null)
            {
                RefillTasks(j);
                return;
            }

            bool island = _tab == IslandTab;
            _sectionTitle.text = SectionTitles[_tab];
            _islandA.gameObject.SetActive(island);
            _islandB.gameObject.SetActive(island);
            SetActive(_resetButton.gameObject, island && _onReset != null);
            _sectionBar.Root.gameObject.SetActive(!island);
            if (island)
            {
                _sectionCount.text = _info.biome ?? "";
                for (int i = 0; i < Pool; i++) SetActive(_cards[i].root.gameObject, false);
                SetActive(_pager.gameObject, false);
                FillIsland(j);
                return;
            }

            var section = (CollectSection)_tab;
            int collectible = CollectionCatalog.CollectibleIn(section), total = CollectionCatalog.CountIn(section);
            _sectionCount.text = j.CollectedIn(section) + "/" + collectible + " gesammelt  ·  " + j.SeenIn(section) + "/" + total + " gesehen";
            _sectionBar.Fill.color = section == CollectSection.SeaSky ? UiStyle.Sky : UiStyle.Mint;
            _sectionBar.Set(section == CollectSection.SeaSky ? j.SeenIn(section) / (float)total : j.CollectedIn(section) / (float)Mathf.Max(1, collectible));

            var entries = CollectionCatalog.EntriesOf(section);
            int pages = PagesOf(_tab, _capacity);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            int first = _page * _capacity;
            for (int i = 0; i < Pool; i++)
            {
                int k = first + i;
                bool shown = i < _capacity && k < entries.Length;
                SetActive(_cards[i].root.gameObject, shown);
                if (shown) FillCard(_cards[i], j, entries[k]);
            }
            SetPager(pages);
        }

        void SetPager(int pages)
        {
            SetActive(_pager.gameObject, pages > 1);
            if (pages > 1)
            {
                _dots.Set(_page, Mathf.Min(pages, MaxPages));
                UiStyle.SetInteractable(_prev, _page > 0);
                UiStyle.SetInteractable(_next, _page < pages - 1);
            }
        }

        public static string TasksProgressLine(PhotoTaskBook tasks) =>
            tasks.DoneCount + "/" + PhotoTaskBook.Total + " erfüllt" + (tasks.AllDone ? "  –  alle geschafft!" : "");

        // Every task as a card: the species (unknown ones stay "???" until seen or photographed), open or done, the
        // day it was done, the move, and the photo in place of the swatch.
        void RefillTasks(DiscoveryJournal j)
        {
            _sectionTitle.text = "Fotoaufgaben";
            _sectionCount.text = TasksProgressLine(_tasks);
            _islandA.gameObject.SetActive(false);
            _islandB.gameObject.SetActive(false);
            SetActive(_resetButton.gameObject, false);
            _sectionBar.Root.gameObject.SetActive(true);
            _sectionBar.Fill.color = UiStyle.Sand;
            _sectionBar.Set(_tasks.DoneCount / (float)Mathf.Max(1, PhotoTaskBook.Total));
            int pages = PagesOf(TasksTab, _capacity);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            int first = _page * _capacity;
            for (int i = 0; i < Pool; i++)
            {
                int k = first + i;
                bool shown = i < _capacity && k < PhotoTaskCatalog.Count;
                SetActive(_cards[i].root.gameObject, shown);
                if (shown) FillTaskCard(_cards[i], j, k);
            }
            SetPager(pages);
        }

        void FillTaskCard(CardView c, DiscoveryJournal j, int task)
        {
            var t = PhotoTaskCatalog.At(task);
            var e = CollectionCatalog.At(t.entry);
            bool done = _tasks.IsDone(task), known = done || j.StateOf(t.entry) != CollectState.Unknown;
            c.entry = known ? t.entry : -1;
            c.task = -1;
            c.button.interactable = known && _onWatch != null;
            Color sw = SwatchOf(e);
            c.body.color = task == _highlightTask ? UiStyle.WithAlpha(UiStyle.Sand, 0.32f)
                : done ? UiStyle.WithAlpha(UiStyle.Sand, 0.16f) : known ? UiStyle.Veil : UiStyle.WithAlpha(UiStyle.Veil, 0.06f);
            c.swatch.color = known ? sw : UiStyle.WithAlpha(UiStyle.Track, 0.6f);
            c.glyph.sprite = JournalGlyphs.Of(JournalGlyphs.For(e));
            c.glyph.color = !known ? UiStyle.Faint : sw.r * 0.3f + sw.g * 0.59f + sw.b * 0.11f > 0.5f ? UiStyle.WithAlpha(UiStyle.Ink, 0.9f) : UiStyle.Cream;
            var picture = done && _thumbOf != null ? _thumbOf(task) : null;
            c.thumb.texture = picture;
            SetActive(c.thumbFrame, picture != null);
            SetActive(c.slot, false);
            SetActive(c.badge, done);
            c.name.text = known ? e.name : "???";
            c.name.color = known ? UiStyle.Cream : UiStyle.Muted;
            c.chip.color = done ? UiStyle.Sand : UiStyle.Ghost;
            c.state.text = done ? "erfüllt" : "offen";
            c.state.color = done ? UiStyle.Ink : UiStyle.CreamSoft;
            c.when.text = done ? PhotoTaskBook.DateLabel(_tasks.DoneAt(task)) : "";
            c.detail.text = t.phrase;
            c.detail.color = done ? UiStyle.CreamSoft : UiStyle.Sand;
            SetTextWidth(c, false);
        }

        static string TabProgress(DiscoveryJournal j, CollectSection s) =>
            s == CollectSection.SeaSky ? j.SeenIn(s) + "/" + CollectionCatalog.CountIn(s) : j.CollectedIn(s) + "/" + CollectionCatalog.CollectibleIn(s);

        void FillCard(CardView c, DiscoveryJournal j, int index)
        {
            var e = CollectionCatalog.At(index);
            var state = j.StateOf(index);
            bool known = state != CollectState.Unknown, collected = state == CollectState.Collected;
            c.entry = known ? index : -1;
            c.button.interactable = known && _onWatch != null;
            Color sw = SwatchOf(e);
            c.body.color = known ? UiStyle.Veil : UiStyle.WithAlpha(UiStyle.Veil, 0.06f);
            c.swatch.color = known ? sw : UiStyle.WithAlpha(UiStyle.Track, 0.6f);
            c.glyph.sprite = JournalGlyphs.Of(JournalGlyphs.For(e));
            c.glyph.color = !known ? UiStyle.Faint : sw.r * 0.3f + sw.g * 0.59f + sw.b * 0.11f > 0.5f ? UiStyle.WithAlpha(UiStyle.Ink, 0.9f) : UiStyle.Cream;
            SetActive(c.badge, collected);
            c.name.text = known ? e.name : "???";
            c.name.color = known ? UiStyle.Cream : UiStyle.Muted;
            c.chip.color = collected ? UiStyle.Mint : known ? UiStyle.WithAlpha(UiStyle.Sand, 0.24f) : UiStyle.Ghost;
            c.state.text = collected ? "gesammelt" : known ? "gesehen" : "unbekannt";
            c.state.color = collected ? UiStyle.Ink : known ? UiStyle.Sand : UiStyle.Muted;
            c.when.text = WhenOf(j, index);
            c.detail.text = DetailOf(j, e, state);
            c.detail.color = collected && j.PresentNow(index) ? UiStyle.CreamSoft : UiStyle.Muted;
            SetActive(c.thumbFrame, false);

            int task = _tasks != null && known ? PhotoTaskCatalog.IndexOfEntry(index) : -1;
            c.task = task;
            SetActive(c.slot, task >= 0);
            SetTextWidth(c, task >= 0);
            if (task < 0) return;
            bool done = _tasks.IsDone(task);
            var picture = done && _thumbOf != null ? _thumbOf(task) : null;
            c.slotPicture.texture = picture;
            SetActive(c.slotRing, !done);
            SetActive(c.slotPictureFrame, picture != null);
            SetActive(c.slotDone, done && picture == null);
            SetActive(c.slotCheck, picture != null);
        }

        // The text column ends before the photo slot when the card has one.
        static void SetTextWidth(CardView c, bool slot)
        {
            float w = TextWidth(slot);
            c.name.rectTransform.sizeDelta = new Vector2(w, 42f);
            c.when.rectTransform.sizeDelta = new Vector2(w - ChipW - WhenGap, 30f);
            c.detail.rectTransform.sizeDelta = new Vector2(w, 30f);
        }

        // The third line of a card: where it lives (unknown), what is missing (seen) or how it is doing on the island.
        public static string DetailOf(DiscoveryJournal j, CollectEntry e, CollectState state)
        {
            if (state == CollectState.Unknown) return (CollectionCatalog.IsRare(e) ? "selten · " : "") + CollectionCatalog.HintOf(e);
            if (state == CollectState.Seen && CollectionCatalog.IsRare(e)) return e.collectible ? "selten · noch nicht auf deiner Insel" : "selten · in freier Wildbahn gesehen";
            if (state == CollectState.Seen) return e.collectible ? "noch nicht auf deiner Insel" : e.type == CollectType.Boat ? "auf dem Meer gesichtet" : "in freier Wildbahn gesehen";
            if (!j.PresentNow(e.index)) return e.type == CollectType.Critter ? "gerade nicht zu sehen" : "zurzeit nicht auf deiner Insel";
            int now = j.CurrentOf(e.index), best = j.BestOf(e.index);
            if (now <= 0) return "lebt auf deiner Insel";
            return best > now ? now + " auf deiner Insel  ·  Rekord " + best : now + " auf deiner Insel";
        }

        void FillIsland(DiscoveryJournal j)
        {
            _sb.Length = 0;
            bool settled = _info.villages > 0 || _info.buildings > 0;
            _sb.Append("Siedlung   ").Append(string.IsNullOrEmpty(_info.stage) ? "Unbesiedelt" : _info.stage).Append('\n');
            _sb.Append("Höchste Stufe dieser Reise   ").Append(IslandSettlementSystem.NameOf((SettlementStage)Mathf.Clamp(j.BestStage, 0, 4))).Append('\n');
            _sb.Append("Bewohner   ").Append(_info.settlers).Append("   ·   Dörfer   ").Append(_info.villages).Append('\n');
            _sb.Append("Gebäude   ").Append(_info.buildings);
            if (settled && !string.IsNullOrEmpty(_info.buildingLine)) _sb.Append('\n').Append(_info.buildingLine);
            else if (!settled) _sb.Append("\nAuf einer großen, bewachsenen Insel\nlassen sich irgendwann Leute nieder.");
            _islandAText.text = _sb.ToString();

            _sb.Length = 0;
            _sb.Append("Tiere   ").Append(_info.animals).Append("   ·   Herden   ").Append(_info.herds).Append('\n');
            _sb.Append("Pflanzen   ").Append(_info.plants).Append("   ·   Bäume   ").Append(_info.trees).Append('\n');
            _sb.Append("Fremde Arten auf der Insel   ").Append(_info.foreignKinds).Append('\n');
            _sb.Append("Jungtiere geboren   ").Append(j.YoungBorn).Append('\n');
            _sb.Append("Blitzfeuer gesehen   ").Append(j.FiresSeen).Append('\n');
            _sb.Append("Inseln aufgenommen   ").Append(_info.absorbed);
            _islandBText.text = _sb.ToString();
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ---------------------------------------------------------------- overlays (photo task, reset)

        RectTransform _overlay, _detailCard, _resetCard;
        RawImage _detailPicture;
        GameObject _detailPictureFrame, _detailEmpty;
        Text _detailName, _detailTaskText, _detailState, _detailHint;
        Button _detailAll;
        int _detailTask = -1, _highlightTask = -1;
        static readonly Vector2 DetailSize = new Vector2(780f, 930f), ResetSize = new Vector2(800f, 740f);

        public bool OverlayOpen => _overlay != null && _overlay.gameObject.activeSelf;
        // The photo task shown in the detail card, -1 while none is up.
        public int DetailTask => OverlayOpen && _detailCard.gameObject.activeSelf ? _detailTask : -1;
        public bool ResetConfirmOpen => OverlayOpen && _resetCard.gameObject.activeSelf;
        public string DetailText => DetailTask >= 0 ? _detailTaskText.text : "";
        public int HighlightTask => _highlightTask;
        public bool ResetButtonShown => _resetButton != null && _resetButton.gameObject.activeSelf;

        // "Fotografiere Flamingos beim Schlammtanz."
        public static string TaskLine(int task)
        {
            var t = PhotoTaskCatalog.At(task);
            return "Fotografiere " + LifeNames.Plural(t.kind) + " " + t.phrase + ".";
        }

        void BuildOverlay()
        {
            _overlay = UiStyle.Rect(_panel, "Overlay");
            _overlay.Stretch();
            // Tapping beside the card closes it; the card's own body swallows its taps.
            var dim = UiStyle.Shape(_overlay, "Dim", UiSprites.RoundedLarge, UiStyle.WithAlpha(UiStyle.Dim, 0.88f), true);
            dim.rectTransform.Stretch();
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.navigation = new Navigation { mode = Navigation.Mode.None };
            dimButton.onClick.AddListener(CloseOverlay);

            _detailCard = UiStyle.Panel(_overlay, "TaskDetail", DetailSize, true).Center(Vector2.zero, DetailSize);
            OverlayLine(_detailCard, "Fotoaufgabe", UiStyle.Subheading, UiStyle.Sand, -40f, 64f, true);
            _detailPicture = UiStyle.Picture(_detailCard, "Photo", new Vector2(340f, 340f), out var frame);
            frame.TopCenter(new Vector2(0f, -122f), new Vector2(340f, 340f));
            _detailPictureFrame = frame.gameObject;
            var empty = UiStyle.Shape(_detailCard, "Empty", UiSprites.Rounded, UiStyle.WithAlpha(UiStyle.Sand, 0.12f));
            empty.rectTransform.TopCenter(new Vector2(0f, -122f), new Vector2(340f, 340f));
            UiStyle.Shape(empty.transform, "Rim", UiSprites.Ring, UiStyle.WithAlpha(UiStyle.Sand, 0.55f)).rectTransform.Stretch();
            UiStyle.Icon(empty.transform, "Icon", UiIcon.Camera, 150f, UiStyle.WithAlpha(UiStyle.Sand, 0.8f));
            _detailEmpty = empty.gameObject;
            _detailName = UiStyle.FitWidth(OverlayLine(_detailCard, "", UiStyle.Subheading, UiStyle.Cream, -484f, 58f, true));
            _detailTaskText = OverlayLine(_detailCard, "", UiStyle.Body, UiStyle.CreamSoft, -548f, 96f);
            _detailState = UiStyle.FitWidth(OverlayLine(_detailCard, "", UiStyle.Caption, UiStyle.Sand, -676f, 40f, true));
            _detailHint = OverlayLine(_detailCard, "", 26, UiStyle.Muted, -722f, 70f);
            var half = new Vector2(340f, UiStyle.ButtonHeightSmall);
            _detailAll = UiStyle.SecondaryButton(_detailCard, "AllTasks", "Alle Aufgaben", half, () => ShowTask(_detailTask));
            ((RectTransform)_detailAll.transform).TopCenter(new Vector2(-180f, -808f), half);
            var ok = UiStyle.PrimaryButton(_detailCard, "Ok", "Schließen", half, CloseOverlay);
            ((RectTransform)ok.transform).TopCenter(new Vector2(180f, -808f), half);

            _resetCard = UiStyle.Panel(_overlay, "ResetConfirm", ResetSize, true).Center(Vector2.zero, ResetSize);
            UiStyle.FitWidth(OverlayLine(_resetCard, "Tagebuch zurücksetzen?", UiStyle.Subheading + 4, UiStyle.Sand, -44f, 70f, true));
            OverlayLine(_resetCard, "Was soll gelöscht werden?\nDas lässt sich nicht rückgängig machen.", UiStyle.Caption + 2, UiStyle.CreamSoft, -132f, 100f);
            var wide = new Vector2(700f, 112f);
            var species = UiStyle.SecondaryButton(_resetCard, "ResetSpecies", JournalReset.SpeciesLabel, wide, () => ConfirmReset(JournalResetScope.SpeciesAndTasks), true);
            ((RectTransform)species.transform).TopCenter(new Vector2(0f, -262f), wide);
            UiStyle.LabelOf(species).fontSize = 34;
            var tall = new Vector2(700f, 136f);
            var all = UiStyle.SecondaryButton(_resetCard, "ResetAll", "", tall, () => ConfirmReset(JournalResetScope.Everything), true);
            ((RectTransform)all.transform).TopCenter(new Vector2(0f, -402f), tall);
            var allLabel = UiStyle.LabelOf(all);
            allLabel.fontSize = 40;
            allLabel.text = JournalReset.EverythingLabel;
            allLabel.rectTransform.Stretch(UiStyle.Gap, 56f, UiStyle.Gap, 12f);
            var note = UiStyle.FitWidth(UiStyle.Label(all.transform, JournalReset.EverythingNote, 27, UiStyle.CreamSoft, TextAnchor.MiddleCenter));
            note.rectTransform.Stretch(UiStyle.Gap, 16f, UiStyle.Gap, 76f);
            var cancel = UiStyle.PrimaryButton(_resetCard, "Cancel", "Abbrechen", wide, CloseOverlay);
            ((RectTransform)cancel.transform).TopCenter(new Vector2(0f, -578f), wide);

            _overlay.gameObject.SetActive(false);
        }

        static Text OverlayLine(RectTransform card, string text, int size, Color color, float y, float height, bool bold = false)
        {
            var t = UiStyle.Label(card, text, size, color, TextAnchor.UpperCenter, bold);
            t.rectTransform.TopCenter(new Vector2(0f, y), new Vector2(card.sizeDelta.x - 80f, height));
            return t;
        }

        // The photo task of a species card's slot: what to photograph, open or done (with its photo).
        public void ShowTaskDetail(int task)
        {
            if (_overlay == null || _tasks == null || task < 0 || task >= PhotoTaskCatalog.Count) return;
            _detailTask = task;
            var t = PhotoTaskCatalog.At(task);
            bool done = _tasks.IsDone(task);
            var picture = done && _thumbOf != null ? _thumbOf(task) : null;
            _detailPicture.texture = picture;
            SetActive(_detailPictureFrame, picture != null);
            SetActive(_detailEmpty, picture == null);
            _detailName.text = CollectionCatalog.At(t.entry).name + "  ·  " + t.move;
            _detailTaskText.text = TaskLine(task);
            _detailState.text = done ? "Erfüllt" + (_tasks.DoneAt(task) != DateTime.MinValue ? " am " + PhotoTaskBook.DateLabel(_tasks.DoneAt(task)) : "") : "Noch offen";
            _detailState.color = done ? UiStyle.Mint : UiStyle.Sand;
            _detailHint.text = done ? "" : "Wenn es so weit ist, zeigt dir ein Kamera-Symbol die Stelle.";
            SetActive(_detailCard.gameObject, true);
            SetActive(_resetCard.gameObject, false);
            _overlay.SetAsLastSibling();
            SetActive(_overlay.gameObject, true);
        }

        public void OpenResetConfirm()
        {
            if (_overlay == null || _onReset == null) return;
            SetActive(_detailCard.gameObject, false);
            SetActive(_resetCard.gameObject, true);
            _overlay.SetAsLastSibling();
            SetActive(_overlay.gameObject, true);
        }

        public void CloseOverlay()
        {
            if (_overlay != null) SetActive(_overlay.gameObject, false);
        }

        // Android back / Escape: an open overlay first; false when there was none (the caller closes the journal).
        public bool Back()
        {
            if (!OverlayOpen) return false;
            CloseOverlay();
            return true;
        }

        // The same route as the dialog's buttons (tests and eval).
        public void ConfirmReset(JournalResetScope scope)
        {
            CloseOverlay();
            _onReset?.Invoke(scope);
            _highlightTask = -1;
            SetTab(0);
        }

        // The tasks view on the page that holds this task, the task's card marked.
        public void ShowTask(int task)
        {
            SetTab(TasksTab, Mathf.Max(0, task) / Mathf.Max(1, _capacity));
            _highlightTask = task;
            Refill();
        }

        // ---------------------------------------------------------------- colours

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        public static Color SwatchOf(CollectEntry e)
        {
            if (e.hasLife)
            {
                switch (e.life)
                {
                    case LifeKind.Grass: return Rgb(126, 190, 96);
                    case LifeKind.Flower: return Rgb(244, 160, 190);
                    case LifeKind.Bush: return Rgb(92, 150, 86);
                    case LifeKind.Tree: return Rgb(70, 132, 80);
                    case LifeKind.Palm: return Rgb(110, 176, 96);
                    case LifeKind.Reed: return Rgb(176, 170, 104);
                    case LifeKind.Hare: return Rgb(168, 130, 96);
                    case LifeKind.Sheep: return Rgb(238, 230, 210);
                    case LifeKind.Goat: return Rgb(160, 156, 148);
                    case LifeKind.Ox: return Rgb(104, 72, 52);
                    case LifeKind.Capybara: return Rgb(150, 108, 72);
                    case LifeKind.Flamingo: return Rgb(250, 150, 160);
                    case LifeKind.Tortoise: return Rgb(120, 140, 92);
                    case LifeKind.Fern: return Rgb(84, 160, 84);
                    case LifeKind.Hibiscus: return Rgb(232, 84, 104);
                    case LifeKind.Banana: return Rgb(236, 206, 84);
                    case LifeKind.Bamboo: return Rgb(150, 196, 96);
                    case LifeKind.JungleTree: return Rgb(44, 112, 72);
                    case LifeKind.Reindeer: return Rgb(150, 124, 104);
                    case LifeKind.Penguin: return Rgb(60, 70, 92);
                    case LifeKind.ArcticFox: return Rgb(234, 240, 246);
                    case LifeKind.Lichen: return Rgb(186, 204, 176);
                    case LifeKind.Mushroom: return Rgb(214, 70, 60);
                    case LifeKind.Heather: return Rgb(176, 120, 176);
                    case LifeKind.Juniper: return Rgb(80, 124, 110);
                    case LifeKind.Spruce: return Rgb(52, 104, 88);
                    case LifeKind.Birch: return Rgb(226, 226, 206);
                    case LifeKind.Meerkat: return Rgb(206, 170, 120);
                    case LifeKind.Zebra: return Rgb(226, 226, 226);
                    case LifeKind.Giraffe: return Rgb(232, 180, 90);
                    case LifeKind.DryGrass: return Rgb(214, 186, 104);
                    case LifeKind.Aloe: return Rgb(110, 170, 130);
                    case LifeKind.ThornBush: return Rgb(140, 130, 84);
                    case LifeKind.TermiteMound: return Rgb(176, 116, 80);
                    case LifeKind.Acacia: return Rgb(128, 156, 72);
                    case LifeKind.Baobab: return Rgb(150, 120, 100);
                    case LifeKind.Crab: return Rgb(230, 96, 70);
                    case LifeKind.Turtle: return Rgb(72, 136, 88);
                    case LifeKind.Butterfly: return Rgb(255, 214, 90);
                    case LifeKind.Firefly: return Rgb(196, 250, 120);
                    case LifeKind.Bird: return Rgb(120, 146, 184);
                    case LifeKind.Seabird: return Rgb(240, 242, 244);
                    default: return UiStyle.Sand;
                }
            }
            if (!e.hasSea) return Rgb(198, 220, 240);
            switch (e.sea)
            {
                case SeaKind.BaitBall: return Rgb(170, 200, 224);
                case SeaKind.FlyingFish: return Rgb(120, 190, 226);
                case SeaKind.Dolphin: return Rgb(124, 150, 176);
                case SeaKind.SeaTurtle: return Rgb(96, 150, 120);
                case SeaKind.Seal: return Rgb(128, 124, 118);
                case SeaKind.Jellyfish: return Rgb(230, 170, 226);
                case SeaKind.Ray: return Rgb(84, 100, 130);
                case SeaKind.Whale: return Rgb(70, 92, 124);
                case SeaKind.WhaleCalf: return Rgb(120, 146, 176);
                case SeaKind.WhaleBull: return Rgb(52, 66, 96);
                case SeaKind.RowBoat: return Rgb(190, 140, 96);
                case SeaKind.SailBoat: return Rgb(246, 240, 224);
                case SeaKind.FishingBoat: return Rgb(96, 160, 170);
                case SeaKind.TradingCog: return Rgb(170, 84, 70);
                default: return UiStyle.Sky;
            }
        }
    }
}
