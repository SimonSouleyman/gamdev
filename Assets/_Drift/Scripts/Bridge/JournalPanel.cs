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
        public const float WideFrom = 2100f;
        const int Pool = 15, MaxPages = 4;
        const float CardW = 424f, CardH = 128f, CardGap = 12f, TabH = 76f, MaxFit = 1.5f;
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
            tab >= IslandTab ? 1 : Mathf.Max(1, (CollectionCatalog.CountIn((CollectSection)tab) + capacity - 1) / Mathf.Max(1, capacity));

        // ---------------------------------------------------------------- build

        Action<int> _onWatch;

        // onWatch gets the catalog index of a card the player tapped (only cards of seen or collected entries).
        public GameObject Build(RectTransform root, Action onClose, Action<int> onWatch = null)
        {
            _onWatch = onWatch;
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
            c.swatch = UiStyle.Dot(c.root, "Swatch", 88f, Color.white);
            c.swatch.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(64f, 0f), new Vector2(88f, 88f));
            UiStyle.Shape(c.swatch.transform, "Shade", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Shadow, 0.35f)).rectTransform.Stretch();
            c.glyph = UiStyle.Shape(c.swatch.transform, "Glyph", null, UiStyle.Ink);
            c.glyph.rectTransform.Center(Vector2.zero, new Vector2(60f, 60f));
            var badge = UiStyle.Dot(c.root, "Badge", 38f, UiStyle.Mint);
            badge.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(98f, -32f), new Vector2(38f, 38f));
            UiStyle.Shape(badge.transform, "Check", JournalGlyphs.Of(JournalGlyph.Check), UiStyle.Ink).rectTransform.Center(Vector2.zero, new Vector2(26f, 26f));
            c.badge = badge.gameObject;

            c.name = UiStyle.FitWidth(UiStyle.Label(c.root, "", 32, UiStyle.Cream, TextAnchor.MiddleLeft, true));
            c.name.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(128f, -32f), new Vector2(236f, 42f));

            // The whole card is the button ("Ansehen"); the eye in the corner says so on every known entry.
            c.body.raycastTarget = true;
            c.button = c.root.gameObject.AddComponent<Button>();
            c.button.targetGraphic = c.body;
            c.button.transition = Selectable.Transition.None;
            c.button.navigation = new Navigation { mode = Navigation.Mode.None };
            c.root.gameObject.AddComponent<UiPressFeedback>().pressedScale = 0.96f;
            var eye = UiStyle.Dot(c.root, "Watch", 44f, UiStyle.WithAlpha(UiStyle.Sand, 0.22f));
            eye.rectTransform.Place(new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-34f, -32f), new Vector2(44f, 44f));
            UiStyle.Icon(eye.rectTransform, "Icon", UiIcon.Camera, 26f, UiStyle.Sand).rectTransform.Center(Vector2.zero, new Vector2(26f, 26f));
            c.go = eye.gameObject;
            c.chip = UiStyle.Pill(c.root, "State", new Vector2(148f, 32f), UiStyle.Ghost);
            c.chip.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(128f, -70f), new Vector2(148f, 32f));
            c.state = UiStyle.Label(c.chip.transform, "", 22, UiStyle.Cream, TextAnchor.MiddleCenter, true);
            c.state.rectTransform.Stretch(6f, 0f, 6f, 2f);
            c.when = UiStyle.FitWidth(UiStyle.Label(c.root, "", 23, UiStyle.Muted, TextAnchor.MiddleLeft));
            c.when.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(288f, -70f), new Vector2(124f, 32f));
            c.detail = UiStyle.FitWidth(UiStyle.Label(c.root, "", 23, UiStyle.CreamSoft, TextAnchor.MiddleLeft));
            c.detail.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(128f, -104f), new Vector2(282f, 32f));
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
            }
            else
            {
                float W = TallSize.x, H = TallSize.y, inner = W - 2f * m;
                _panel.sizeDelta = TallSize;
                Put(_title.rectTransform, m, 22f, inner, 110f);
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
                Put(_islandA, m, 552f, inner, 410f);
                Put(_islandB, m, 552f + 410f + CardGap, inner, 310f);
            }
            _capacity = _columns * 5;
            for (int i = 0; i < Pool; i++)
                Put(_cards[i].root, _gridOrigin.x + (i % _columns) * (CardW + CardGap), _gridOrigin.y + (i / _columns) * (CardH + CardGap), CardW, CardH);
            Refill();
        }

        // ---------------------------------------------------------------- navigation

        public void SetTab(int tab, int page = 0)
        {
            _tab = Mathf.Clamp(tab, 0, TabCount - 1);
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

        public static string ProgressLine(DiscoveryJournal j) =>
            "Gesammelt " + j.CollectedCount + "/" + CollectionCatalog.CollectibleCount + "  ·  Gesehen " + j.SeenCount + "/" + CollectionCatalog.Count
            + (j.Complete ? "  –  alles gefunden!" : "");

        void Refill()
        {
            var j = _journal;
            if (j == null || _screen == null) return;
            _count.text = ProgressLine(j);
            _bar.Set(j.CollectedCount / (float)CollectionCatalog.CollectibleCount);
            _book.text = _bookRef != null ? _bookRef.Line : "";

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

            bool island = _tab == IslandTab;
            _sectionTitle.text = SectionTitles[_tab];
            _islandA.gameObject.SetActive(island);
            _islandB.gameObject.SetActive(island);
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
            SetActive(_pager.gameObject, pages > 1);
            if (pages > 1)
            {
                _dots.Set(_page, Mathf.Min(pages, MaxPages));
                UiStyle.SetInteractable(_prev, _page > 0);
                UiStyle.SetInteractable(_next, _page < pages - 1);
            }
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
            SetActive(c.go, known && _onWatch != null);
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
            c.when.text = collected ? "seit " + DiscoveryJournal.FormatTime(j.CollectedAt(index)) : known ? "bei " + DiscoveryJournal.FormatTime(j.SeenAt(index)) : "";
            c.detail.text = DetailOf(j, e, state);
            c.detail.color = collected && j.PresentNow(index) ? UiStyle.CreamSoft : UiStyle.Muted;
        }

        // The third line of a card: where it lives (unknown), what is missing (seen) or how it is doing on the island.
        public static string DetailOf(DiscoveryJournal j, CollectEntry e, CollectState state)
        {
            if (state == CollectState.Unknown) return CollectionCatalog.HintOf(e);
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
