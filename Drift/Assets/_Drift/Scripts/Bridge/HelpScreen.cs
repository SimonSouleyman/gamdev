using System;
using Drift.Audio;
using Drift.Core;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Which pages the Anleitung shows: inside a run only that mode's pages, on the title both (with two tabs).
    public enum HelpScope { Both, Cozy, Adventure }

    // "Anleitung": paged cards with Tilda in the corner. A plain builder/controller that SessionScreens
    // places into its own canvas, so it needs no scene wiring and shares the title / pause raycaster. On wide
    // screens SessionScreens hands over the big presenter Tilda beside the panel (UsePresenter); the small one in
    // the corner then hides and her speech bubble takes the full width. Her tip for the page is typed into the
    // bubble (TildaBubble) while she grumbles along.
    public sealed class HelpScreen
    {
        public enum PageId { Goal, Controls, Buoyancy, World, Life, AdventureRing, AdventureControls, AdventureRoute }

        public const int PageCount = 8;
        const float PanelWidth = 960f, PanelHeight = 1680f, ContentWidth = 880f, ContentTop = -150f;
        public static readonly Vector2 PanelSize = new Vector2(PanelWidth, PanelHeight);

        static readonly PageId[] CozyPages = { PageId.Goal, PageId.Controls, PageId.Buoyancy, PageId.World, PageId.Life };
        static readonly PageId[] AdventurePages = { PageId.AdventureRing, PageId.AdventureControls, PageId.AdventureRoute };
        static readonly PageId[] AllPages = { PageId.Goal, PageId.Controls, PageId.Buoyancy, PageId.World, PageId.Life, PageId.AdventureRing, PageId.AdventureControls, PageId.AdventureRoute };

        public static PageId[] PagesFor(HelpScope scope) => scope == HelpScope.Cozy ? CozyPages : scope == HelpScope.Adventure ? AdventurePages : AllPages;
        public static GameMode ModeOf(PageId id) => id >= PageId.AdventureRing ? GameMode.Adventure : GameMode.Cozy;
        // The title explains both games; a paused run only its own.
        public static HelpScope ScopeFor(bool onTitle, GameMode mode) => onTitle ? HelpScope.Both : mode == GameMode.Adventure ? HelpScope.Adventure : HelpScope.Cozy;

        static readonly string[] Titles = { "Dein Ziel", "Steuerung", "Auftrieb & Form", "Platten, Stürme, Vulkane", "Leben beobachten", "Abenteuer", "Steuerung", "Ausweichen & Treibgut" };
        // Two lines in the narrow bubble beside the small Tilda: above it the page's cards end, below it the dots begin.
        static readonly string[] Tips =
        {
            "Hallo, ich bin " + TildaBubble.Key("Tilda") + "! Hier steht alles zum Treiben.",
            "Ich lenke ganz " + TildaBubble.Key("gemütlich") + " – Inseln brauchen ihre Zeit.",
            TildaBubble.Key("Rund ist gesund.") + " Sagt ein Vulkan mit rundem Bauch!",
            "Vulkaninseln sind meine " + TildaBubble.Key("Verwandten") + " – alle freundlich!",
            "Schau genau hin – auf meinen Hängen wächst es " + TildaBubble.Key("grün") + ".",
            "Im " + TildaBubble.Key("Abenteuer") + " zählt jeder Meter – ich feuere dich an!",
            "Deine Insel " + TildaBubble.Key("fährt von allein") + " – du lenkst nur zur Seite.",
            "Weich den Inseln aus und " + TildaBubble.Key("schnapp dir Treibgut") + "!",
        };
        // Tilda grumbles while the tip is typed; page 1 waves, the volcano page gets a little lava cheer.
        static readonly TildaPose[] Poses = { TildaPose.Wave, TildaPose.Talk, TildaPose.Talk, TildaPose.Cheer, TildaPose.Talk, TildaPose.Cheer, TildaPose.Talk, TildaPose.Cheer };
        const float GestureSeconds = 1.8f, TipWidth = 640f, TipBottom = -1358f, CornerSize = 270f;
        static readonly Vector2 CornerPos = new Vector2(10f, -1096f);
        static readonly Vector2 TabSize = new Vector2(300f, 88f);

        GameObject _screen;
        readonly GameObject[] _pages = new GameObject[PageCount];
        readonly Image[] _dots = new Image[PageCount];
        RectTransform _dotRow, _tabs;
        // Per tab: the mint "selected" button and the lagoon one, swapped instead of recoloured.
        readonly GameObject[] _tabOn = new GameObject[2], _tabOff = new GameObject[2];
        Text _title, _nextLabel, _replayLabel, _voiceLabel, _cozyStickLabel;
        TildaBubble _tip;
        RectTransform _panel;
        string _remarkKey;
        static HelpScreen s_listening;
        TildaView _tilda, _presenter;
        Button _back, _replay;
        RectTransform[] _desktopCards = new RectTransform[2], _touchCards = new RectTransform[2];
        Action _onClose, _onToggleVoice;
        Action<GameMode> _onReplay;
        Func<GameMode, bool> _replayPending;
        HelpScope _scope = HelpScope.Both;
        PageId[] _sequence = AllPages;
        int _page;
        float _talkUntil;

        TildaView ActiveView => _presenter != null ? _presenter : _tilda;

        public bool IsOpen => _screen != null && _screen.activeSelf;
        // Index into the pages of the current scope.
        public int Page => _page;
        public HelpScope Scope => _scope;
        public int VisiblePageCount => _sequence.Length;
        public PageId CurrentPage => _sequence[Mathf.Clamp(_page, 0, _sequence.Length - 1)];
        public GameMode CurrentMode => ModeOf(CurrentPage);
        public string CurrentTitle => Titles[(int)CurrentPage];
        public bool TabsShown => _tabs != null && _tabs.gameObject.activeSelf;
        public GameObject Root => _screen;

        public static string TitleOf(PageId id) => Titles[(int)id];

        // onReplay gets the mode whose tutorial should run again (the page's mode); replayPending tells, per mode,
        // whether that tutorial is already queued.
        public void Build(RectTransform root, Action onClose, Action<GameMode> onReplay, Action onToggleVoice = null, Func<GameMode, bool> replayPending = null)
        {
            _onClose = onClose;
            _onReplay = onReplay;
            _replayPending = replayPending;
            _onToggleVoice = onToggleVoice;
            _presenter = null;
            _remarkKey = null;
            if (s_listening != null)
            {
                TildaVoice.LineStarted -= s_listening.OnLineStarted;
                TildaVoice.LineEnded -= s_listening.OnLineEnded;
            }
            s_listening = this;
            TildaVoice.LineStarted += OnLineStarted;
            TildaVoice.LineEnded += OnLineEnded;
            var scrim = UiStyle.Scrim(root, "HelpScreen", UiStyle.Dim);
            _screen = scrim.gameObject;
            var panel = UiStyle.Panel(scrim, "Panel", new Vector2(PanelWidth, PanelHeight), true).Center(Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            UiStyle.FadeIn(_screen, panel);

            _title = UiStyle.Label(panel, "", UiStyle.Heading, UiStyle.Sand, TextAnchor.MiddleCenter, true);
            _title.rectTransform.TopCenter(new Vector2(0f, -36f), new Vector2(700f, 80f));
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var close = UiStyle.IconButton(panel, "CloseHelp", 88f, Close);
            ((RectTransform)close.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -32f), new Vector2(88f, 88f));
            Cross(close.transform, 40f);
            UiStyle.Divider(panel, ContentWidth).TopCenter(new Vector2(0f, -128f), new Vector2(ContentWidth, 3f));

            _pages[(int)PageId.Goal] = BuildGoal(panel);
            _pages[(int)PageId.Controls] = BuildControls(panel);
            _pages[(int)PageId.Buoyancy] = BuildBuoyancy(panel);
            _pages[(int)PageId.World] = BuildWorld(panel);
            _pages[(int)PageId.Life] = BuildLife(panel);
            _pages[(int)PageId.AdventureRing] = BuildAdventureRing(panel);
            _pages[(int)PageId.AdventureControls] = BuildAdventureControls(panel);
            _pages[(int)PageId.AdventureRoute] = BuildAdventureRoute(panel);

            // The title's Anleitung explains both games: two tabs stand on the panel's top edge, like folder tabs.
            _tabs = UiStyle.Rect(panel, "ModeTabs").Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(2f * TabSize.x + 16f, TabSize.y));
            BuildTab(0, GameMode.Cozy, -(TabSize.x + 16f) * 0.5f);
            BuildTab(1, GameMode.Adventure, (TabSize.x + 16f) * 0.5f);

            _panel = panel;
            var portrait = TildaPortrait.CreateImage(panel, new Vector2(CornerSize, CornerSize));
            portrait.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), CornerPos, new Vector2(CornerSize, CornerSize));
            _tilda = portrait.GetComponent<TildaView>();
            // After her picture, so the tail lies over her flank. It grows upwards from just above the page dots.
            _tip = TildaBubble.Create(panel, "Tip", TipWidth, false);
            PlaceTip();

            var dots = _dotRow = UiStyle.Rect(panel, "Dots").TopCenter(new Vector2(0f, -1370f), new Vector2(PageCount * 48f, 32f));
            for (int i = 0; i < PageCount; i++)
            {
                _dots[i] = UiStyle.Dot(dots, "Dot" + i, 22f, UiStyle.Faint);
                _dots[i].rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24f + i * 48f, 0f), new Vector2(22f, 22f));
            }

            _back = UiStyle.SecondaryButton(panel, "HelpBack", "Zurück", new Vector2(420f, 112f), () => Show(_page - 1));
            ((RectTransform)_back.transform).TopCenter(new Vector2(-230f, -1420f), new Vector2(420f, 112f));
            var next = UiStyle.PrimaryButton(panel, "HelpNext", "Weiter", new Vector2(420f, 112f), Next);
            ((RectTransform)next.transform).TopCenter(new Vector2(230f, -1420f), new Vector2(420f, 112f));
            _nextLabel = UiStyle.LabelOf(next);

            _replay = UiStyle.SecondaryButton(panel, "ReplayTutorial", "Tutorial erneut spielen", new Vector2(470f, 80f), Replay);
            ((RectTransform)_replay.transform).TopCenter(new Vector2(-205f, -1560f), new Vector2(470f, 80f));
            _replayLabel = UiStyle.LabelOf(_replay);
            _replayLabel.fontSize = 34;
            _replayLabel.fontStyle = FontStyle.Bold;

            var voice = UiStyle.SecondaryButton(panel, "VoiceToggle", TildaVoice.ToggleLabel, new Vector2(390f, 80f), ToggleVoice);
            ((RectTransform)voice.transform).TopCenter(new Vector2(245f, -1560f), new Vector2(390f, 80f));
            _voiceLabel = UiStyle.LabelOf(voice);
            _voiceLabel.fontSize = 34;
            _voiceLabel.fontStyle = FontStyle.Bold;

            _screen.SetActive(false);
        }

        // page indexes the scope's own pages.
        public void Open(HelpScope scope, int page, bool touchFirst)
        {
            if (_screen == null) return;
            _scope = scope;
            _sequence = PagesFor(scope);
            if (_tabs != null && _tabs.gameObject.activeSelf != (scope == HelpScope.Both)) _tabs.gameObject.SetActive(scope == HelpScope.Both);
            ArrangeControls(touchFirst);
            // The cozy steering scheme can change in the pause menu between two openings.
            if (_cozyStickLabel != null) _cozyStickLabel.text = ModeTexts.SteerHint(GameMode.Cozy, true, Drift.Islands.Island.DirectionSteering);
            RefreshVoiceLabel();
            for (int i = 0; i < PageCount; i++)
                if (_pages[i] != null && _pages[i].activeSelf) _pages[i].SetActive(false);
            _screen.SetActive(true);
            _screen.transform.SetAsLastSibling();
            Show(page);
        }

        // A tab jumps to the first page of its mode.
        public void ShowMode(GameMode mode)
        {
            for (int i = 0; i < _sequence.Length; i++)
                if (ModeOf(_sequence[i]) == mode) { Show(i); return; }
        }

        public void Hide()
        {
            if (_screen == null || !_screen.activeSelf) return;
            _screen.SetActive(false);
            if (Application.isPlaying) TildaVoice.Quiet();
        }

        // The tip is typed while she grumbles it; her mouth moves for as long as that takes.
        void ShowTip()
        {
            PlaceTip();
            if (!Application.isPlaying)
            {
                _tip.ShowStill(Tips[(int)CurrentPage]);
                PlaceTip();
                return;
            }
            var mood = Poses[(int)CurrentPage] == TildaPose.Wave ? GrumbleMood.Cheerful : Poses[(int)CurrentPage] == TildaPose.Cheer ? GrumbleMood.Giggly : GrumbleMood.Warm;
            _tip.Show(Tips[(int)CurrentPage], mood, TildaBubble.RemarkCharsPerSecond, true);
            PlaceTip();
            _talkUntil = Time.unscaledTime + GrumbleTiming.Duration(TildaBubble.Plain(Tips[(int)CurrentPage]), TildaBubble.RemarkCharsPerSecond);
        }

        // Small Tilda in the corner: bubble to her right, tail at her mouth. Big Tilda beside the panel: the bubble
        // takes the full width and its tail reaches out of the panel towards her.
        void PlaceTip()
        {
            if (_tip == null) return;
            bool big = _presenter != null;
            _tip.SetWidth(big ? ContentWidth : TipWidth);
            _tip.Rect.Place(new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(-40f, TipBottom), new Vector2(_tip.Width, _tip.Height));
            Vector3 mouth;
            float gap;
            if (big)
            {
                var figure = (RectTransform)_presenter.transform;
                Vector2 m = TildaPortrait.MouthIn(true);
                mouth = figure.TransformPoint(new Vector3((m.x - figure.pivot.x) * figure.rect.width, (m.y - figure.pivot.y) * figure.rect.height, 0f));
                gap = 0.2f * figure.rect.width;
            }
            else
            {
                Vector2 m = TildaPortrait.MouthIn(false);
                mouth = _panel.TransformPoint(new Vector3(-PanelWidth * 0.5f + CornerPos.x + m.x * CornerSize, PanelHeight * 0.5f + CornerPos.y - (1f - m.y) * CornerSize, 0f));
                gap = 0.22f * CornerSize;
            }
            _tip.PointTailAt(mouth, gap);
        }

        // A remark while the Anleitung is up and no big Tilda stands beside it (portrait): the tip bubble says it,
        // then the page's tip comes back.
        void OnLineStarted(TildaLine line)
        {
            if (line.shown || !IsOpen || _presenter != null || !Application.isPlaying) return;
            line.shown = true;
            _remarkKey = line.key;
            string key = line.key;
            _tip.Show(line.text, line.mood, TildaBubble.RemarkCharsPerSecond, true, () => TildaVoice.Hush(key));
            PlaceTip();
            _talkUntil = Time.unscaledTime + GrumbleTiming.Duration(TildaBubble.Plain(line.text), TildaBubble.RemarkCharsPerSecond);
            Pose();
        }

        void OnLineEnded(string key)
        {
            if (key != _remarkKey) return;
            _remarkKey = null;
            if (!IsOpen) return;
            ShowTip();
            Pose();
        }

        public void Show(int page)
        {
            _page = Mathf.Clamp(page, 0, _sequence.Length - 1);
            var current = CurrentPage;
            for (int i = 0; i < PageCount; i++)
            {
                bool shown = i == (int)current;
                if (_pages[i] != null && _pages[i].activeSelf != shown) _pages[i].SetActive(shown);
            }
            // The dots count the pages of the current mode only; with both modes the tabs tell which one that is.
            var mode = ModeOf(current);
            int first = 0, count = 0;
            for (int i = 0; i < _sequence.Length; i++)
            {
                if (ModeOf(_sequence[i]) != mode) continue;
                if (count == 0) first = i;
                count++;
            }
            _dotRow.sizeDelta = new Vector2(count * 48f, 32f);
            for (int i = 0; i < PageCount; i++)
            {
                bool used = i < count;
                if (_dots[i].gameObject.activeSelf != used) _dots[i].gameObject.SetActive(used);
                bool on = used && first + i == _page;
                _dots[i].color = on ? UiStyle.Mint : UiStyle.Faint;
                _dots[i].rectTransform.sizeDelta = on ? new Vector2(30f, 30f) : new Vector2(22f, 22f);
            }
            for (int t = 0; t < 2; t++)
            {
                bool selected = (t == 0 ? GameMode.Cozy : GameMode.Adventure) == mode;
                if (_tabOn[t] != null && _tabOn[t].activeSelf != selected) _tabOn[t].SetActive(selected);
                if (_tabOff[t] != null && _tabOff[t].activeSelf == selected) _tabOff[t].SetActive(!selected);
            }
            _title.text = Titles[(int)current];
            _remarkKey = null;
            RefreshReplay();
            ShowTip();
            Pose();
            _back.gameObject.SetActive(_page > 0);
            _nextLabel.text = _page == _sequence.Length - 1 ? "Fertig" : "Weiter";
        }

        // The big Tilda beside the panel takes over (null: back to the small one in the corner).
        public void UsePresenter(TildaView presenter)
        {
            if (presenter == _presenter || _tilda == null) return;
            _presenter = presenter;
            bool big = presenter != null;
            if (_tilda.gameObject.activeSelf == big) _tilda.gameObject.SetActive(!big);
            if (!Application.isPlaying && IsOpen) _tip.ShowStill(Tips[(int)CurrentPage]);
            PlaceTip();
            if (IsOpen) Pose();
        }

        // Page 1 waves, the volcano page cheers for a moment; the big Tilda then presents the card while she talks.
        void Pose()
        {
            var view = ActiveView;
            if (view == null) return;
            var gesture = Poses[(int)CurrentPage];
            bool big = _presenter != null;
            if (!Application.isPlaying)
            {
                view.SetPose(big && gesture == TildaPose.Talk ? TildaPose.Present : gesture, true);
                return;
            }
            float talk = Mathf.Max(0f, _talkUntil - Time.unscaledTime);
            view.SetPose(TildaPose.Idle);
            if (big) view.Play(gesture == TildaPose.Talk ? TildaPose.Present : gesture, gesture == TildaPose.Talk ? talk : GestureSeconds, talk, TildaPose.Present, talk);
            else view.Play(gesture, gesture == TildaPose.Talk ? 0f : GestureSeconds, talk);
        }

        public void RefreshVoiceLabel()
        {
            if (_voiceLabel != null) _voiceLabel.text = TildaVoice.ToggleLabel;
        }

        void ToggleVoice()
        {
            if (_onToggleVoice != null) _onToggleVoice();
            else TildaVoice.Toggle();
            RefreshVoiceLabel();
        }

        // "Tutorial erneut spielen" restarts the tutorial of the page's mode.
        void RefreshReplay() => SetReplayPending(_replayPending != null && _replayPending(CurrentMode));

        public void SetReplayPending(bool pending)
        {
            if (_replayLabel == null) return;
            _replayLabel.text = pending ? "Tilda ist wieder dabei" : "Tutorial erneut spielen";
            _replayLabel.color = pending ? UiStyle.Mint : UiStyle.Cream;
            _replay.interactable = !pending;
        }

        void Next()
        {
            if (_page >= _sequence.Length - 1) Close();
            else Show(_page + 1);
        }

        void Close()
        {
            Hide();
            _onClose?.Invoke();
        }

        void Replay()
        {
            _onReplay?.Invoke(CurrentMode);
            SetReplayPending(true);
        }

        void ArrangeControls(bool touchFirst)
        {
            for (int i = 0; i < 2; i++)
            {
                if (_desktopCards[i] == null || _touchCards[i] == null) continue;
                var first = touchFirst ? _touchCards[i] : _desktopCards[i];
                var second = touchFirst ? _desktopCards[i] : _touchCards[i];
                float x = first.anchoredPosition.x;
                first.anchoredPosition = new Vector2(x, 0f);
                second.anchoredPosition = new Vector2(x, -first.sizeDelta.y - 20f);
            }
        }

        void BuildTab(int index, GameMode mode, float x)
        {
            string label = GameModes.Label(mode);
            var on = UiStyle.PrimaryButton(_tabs, "Tab" + mode + "On", label, TabSize, () => ShowMode(mode));
            ((RectTransform)on.transform).Center(new Vector2(x, 0f), TabSize);
            var off = UiStyle.SecondaryButton(_tabs, "Tab" + mode, label, TabSize, () => ShowMode(mode), false, true);
            ((RectTransform)off.transform).Center(new Vector2(x, 0f), TabSize);
            UiStyle.FitWidth(UiStyle.LabelOf(on));
            UiStyle.FitWidth(UiStyle.LabelOf(off));
            _tabOn[index] = on.gameObject;
            _tabOff[index] = off.gameObject;
        }

        // ---------------------------------------------------------------- pages

        static RectTransform NewPage(RectTransform panel, string name) =>
            UiStyle.Rect(panel, name).TopCenter(new Vector2(0f, ContentTop), new Vector2(ContentWidth, 960f));

        static RectTransform InfoCard(RectTransform page, float y, float height, Color dot, string title, string body, int bodySize = 32)
        {
            var card = UiStyle.Card(page, "Card", new Vector2(ContentWidth, height)).TopCenter(new Vector2(0f, y), new Vector2(ContentWidth, height));
            UiStyle.Dot(card, "Dot", 30f, dot).rectTransform.TopLeft(new Vector2(34f, -30f), new Vector2(30f, 30f));
            var t = UiStyle.Label(card, title, 38, UiStyle.Sand, TextAnchor.UpperLeft, true);
            t.rectTransform.TopLeft(new Vector2(86f, -20f), new Vector2(760f, 50f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var b = UiStyle.Label(card, body, bodySize, UiStyle.CreamSoft, TextAnchor.UpperLeft);
            b.rectTransform.TopLeft(new Vector2(86f, -74f), new Vector2(760f, height - 90f));
            return card;
        }

        GameObject BuildGoal(RectTransform panel)
        {
            var page = NewPage(panel, "PageGoal");
            var art = UiStyle.Card(page, "Art", new Vector2(ContentWidth, 280f)).TopCenter(Vector2.zero, new Vector2(ContentWidth, 280f));
            UiStyle.Dot(art, "You", 120f, UiStyle.Sand).rectTransform.Center(new Vector2(-310f, 0f), new Vector2(120f, 120f));
            var arrow = UiStyle.Shape(art, "Arrow", UiSprites.Arrow, UiStyle.Cream);
            arrow.rectTransform.Center(new Vector2(-205f, 0f), new Vector2(64f, 64f));
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            UiStyle.Dot(art, "Other", 92f, UiStyle.Mint).rectTransform.Center(new Vector2(-105f, 0f), new Vector2(92f, 92f));
            var eq = UiStyle.Label(art, "=", 80, UiStyle.Cream, TextAnchor.MiddleCenter, true);
            eq.rectTransform.Center(new Vector2(35f, 4f), new Vector2(100f, 100f));
            UiStyle.Dot(art, "MergedA", 150f, UiStyle.Sand).rectTransform.Center(new Vector2(200f, -8f), new Vector2(150f, 150f));
            UiStyle.Dot(art, "MergedB", 112f, UiStyle.Sand).rectTransform.Center(new Vector2(290f, 22f), new Vector2(112f, 112f));
            UiStyle.Dot(art, "MergedHill", 46f, UiStyle.Mint).rectTransform.Center(new Vector2(250f, 8f), new Vector2(46f, 46f));

            InfoCard(page, -300f, 204f, UiStyle.Mint, "Rammen und wachsen", "Steuere deine Insel in andere Inseln hinein. Sie wachsen an, und deine Insel wird größer.");
            InfoCard(page, -524f, 204f, UiStyle.Sky, "Eine Pangäa bauen", "Sammle alle Inseln der Welt zu einem Kontinent. Keine Eile: Im gemütlichen Spiel geht deine Insel nie unter.");
            InfoCard(page, -748f, 204f, UiStyle.Sand, "Eine runde Welt", "Das Meer ist rundherum verbunden: Wer über den Rand fährt, kommt gegenüber wieder an.");
            return page.gameObject;
        }

        GameObject BuildControls(RectTransform panel)
        {
            var page = NewPage(panel, "PageControls");

            var desktop = _desktopCards[0] = UiStyle.Card(page, "Desktop", new Vector2(ContentWidth, 470f)).TopCenter(Vector2.zero, new Vector2(ContentWidth, 470f));
            CardHeading(desktop, "Am Computer");
            KeyRow(desktop, -92f, "Richtung: vor und zurück", "W", "S");
            KeyRow(desktop, -166f, "Richtung: nach links und rechts", "A", "D");
            KeyRow(desktop, -240f, "oder Mausrad: Zoom", "Q", "E");
            KeyRow(desktop, -314f, "Pause", "Esc");
            KeyRow(desktop, -388f, "auf ein Tier: ansehen", "Klick");

            var touch = _touchCards[0] = UiStyle.Card(page, "Touch", new Vector2(ContentWidth, 430f)).TopCenter(new Vector2(0f, -490f), new Vector2(ContentWidth, 430f));
            CardHeading(touch, "Am Handy");
            var stick = GlyphRow(touch, -96f, ModeTexts.SteerHint(GameMode.Cozy, true, Drift.Islands.Island.DirectionSteering), out _cozyStickLabel);
            StickGlyph(stick);
            var pinch = GlyphRow(touch, -204f, "Zwei Finger: Zoom");
            Finger(pinch, new Vector2(-22f, -14f));
            Finger(pinch, new Vector2(22f, 14f));
            var tap = GlyphRow(touch, -312f, "Tippen: Tier ansehen");
            UiStyle.Dot(tap, "Ripple", 84f, UiStyle.WithAlpha(UiStyle.Mint, 0.25f)).rectTransform.Center(Vector2.zero, new Vector2(84f, 84f));
            Finger(tap, Vector2.zero);
            return page.gameObject;
        }

        static void StickGlyph(RectTransform glyph)
        {
            UiStyle.Dot(glyph, "Base", 84f, UiStyle.Ghost).rectTransform.Center(Vector2.zero, new Vector2(84f, 84f));
            UiStyle.Dot(glyph, "Knob", 40f, UiStyle.WithAlpha(UiStyle.Cream, 0.85f)).rectTransform.Center(new Vector2(12f, 10f), new Vector2(40f, 40f));
        }

        static void CardHeading(RectTransform card, string text)
        {
            var t = UiStyle.Label(card, text, 38, UiStyle.Sand, TextAnchor.UpperLeft, true);
            t.rectTransform.TopLeft(new Vector2(36f, -22f), new Vector2(800f, 50f));
        }

        static void KeyRow(RectTransform card, float y, string text, params string[] keys)
        {
            float x = 36f;
            foreach (var k in keys)
            {
                float w = k.Length > 1 ? 118f : 62f;
                UiStyle.KeyCap(card, k, new Vector2(w, 58f)).TopLeft(new Vector2(x, y), new Vector2(w, 58f));
                x += w + 12f;
            }
            var t = UiStyle.Label(card, text, 32, UiStyle.CreamSoft, TextAnchor.MiddleLeft);
            t.rectTransform.TopLeft(new Vector2(Mathf.Max(x + 12f, 196f), y), new Vector2(640f, 58f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        static RectTransform GlyphRow(RectTransform card, float y, string text) => GlyphRow(card, y, text, out _);

        static RectTransform GlyphRow(RectTransform card, float y, string text, out Text label)
        {
            var t = label = UiStyle.Label(card, text, 32, UiStyle.CreamSoft, TextAnchor.MiddleLeft);
            t.rectTransform.TopLeft(new Vector2(196f, y), new Vector2(640f, 92f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return UiStyle.Rect(card, "Glyph").TopLeft(new Vector2(56f, y), new Vector2(92f, 92f));
        }

        static void Finger(RectTransform parent, Vector2 pos)
        {
            UiStyle.Dot(parent, "FingerGlow", 50f, UiStyle.WithAlpha(UiStyle.Cream, 0.3f)).rectTransform.Center(pos, new Vector2(50f, 50f));
            UiStyle.Dot(parent, "Finger", 32f, UiStyle.Cream).rectTransform.Center(pos, new Vector2(32f, 32f));
        }

        GameObject BuildBuoyancy(RectTransform panel)
        {
            var page = NewPage(panel, "PageBuoyancy");
            var buoy = InfoCard(page, 0f, 330f, UiStyle.Sky, "Auftrieb", "Der Balken zeigt, wie hoch deine Insel schwimmt. Sie sinkt langsam – jede neue Insel hebt sie wieder an.");
            SampleBar(buoy, -206f, 0.78f, UiStyle.Sky, "schwimmt hoch");
            SampleBar(buoy, -262f, 0.34f, UiStyle.Sand, "sinkt langsam");

            var form = InfoCard(page, -350f, 330f, UiStyle.Mint, "Form", "Runde Inseln schwimmen am besten. Längliche Inseln liegen tiefer und sinken bis zu 1,6-mal schneller.");
            UiStyle.Dot(form, "Round", 96f, UiStyle.Mint).rectTransform.TopLeft(new Vector2(110f, -196f), new Vector2(96f, 96f));
            var roundText = UiStyle.Label(form, "rund", UiStyle.Caption, UiStyle.Mint, TextAnchor.MiddleLeft);
            roundText.rectTransform.TopLeft(new Vector2(226f, -214f), new Vector2(200f, 60f));
            var longShape = UiStyle.Pill(form, "Long", new Vector2(230f, 48f), UiStyle.Coral);
            longShape.rectTransform.TopLeft(new Vector2(420f, -220f), new Vector2(230f, 48f));
            var longText = UiStyle.Label(form, "länglich", UiStyle.Caption, UiStyle.Coral, TextAnchor.MiddleLeft);
            longText.rectTransform.TopLeft(new Vector2(670f, -214f), new Vector2(200f, 60f));

            InfoCard(page, -700f, 250f, UiStyle.Sand, "Die Insel dreht sich", "Bei jedem Treffer dreht sich deine Insel ein Stück – je nachdem, wo du getroffen hast. So wächst sie von allen Seiten und bleibt schön rund.");
            return page.gameObject;
        }

        static void SampleBar(RectTransform card, float y, float value, Color color, string caption)
        {
            var bar = UiStyle.Bar(card, "SampleBar", new Vector2(300f, 24f), color);
            bar.Root.TopLeft(new Vector2(86f, y), new Vector2(300f, 24f));
            bar.Set(value);
            var t = UiStyle.Label(card, caption, UiStyle.Caption, color, TextAnchor.MiddleLeft);
            t.rectTransform.TopLeft(new Vector2(410f, y + 12f), new Vector2(440f, 48f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        GameObject BuildWorld(RectTransform panel)
        {
            var page = NewPage(panel, "PageWorld");
            InfoCard(page, 0f, 290f, UiStyle.Sky, "Strömungen", "Unter dem Meer wandern große Platten. Ihre Strömungen tragen dich und alle Inseln mit – nutze sie wie Rückenwind.");
            InfoCard(page, -310f, 290f, UiStyle.Sand, "Stürme", "Im Sturm liegt deine Insel tiefer, und Blitze können Feuer entzünden. Keine Angst: Das Grün wächst wieder nach.");
            InfoCard(page, -620f, 290f, UiStyle.Coral, "Vulkane", "Manchmal steigt eine Vulkaninsel aus dem Meer. Sie gibt besonders viel Auftrieb – hol sie dir!");
            return page.gameObject;
        }

        GameObject BuildLife(RectTransform panel)
        {
            var page = NewPage(panel, "PageLife");
            float y = 0f;
            void Row(Color dot, string title, string body)
            {
                InfoCard(page, y, 176f, dot, title, body, 30);
                y -= 192f;
            }
            Row(UiStyle.Mint, "Herden und Jungtiere", "Hasen, Schafe, Ziegen und Ochsen ziehen in Herden. Tagsüber kommen Jungtiere zur Welt.");
            Row(UiStyle.Sky, "Nacht", "Nachts schlafen die Herden, ein Wächter bleibt wach – und Glühwürmchen leuchten.");
            Row(UiStyle.Sand, "Tier ansehen", "Tippe oder klicke ein Tier an. Mit „Herde folgen“ geht die Kamera mit der Herde mit.");
            Row(UiStyle.Cream, "Tagebuch", "Das Buch oben rechts merkt sich jede Art, die du entdeckt hast.");
            Row(UiStyle.Coral, "Foto", "Im Pausenmenü wartet der Fotomodus für schöne Bilder deiner Insel.");
            return page.gameObject;
        }

        // Abenteuer has its own three pages; inside an adventure run they are the whole Anleitung.
        GameObject BuildAdventureRing(RectTransform panel)
        {
            var page = NewPage(panel, "PageAdventureRing");
            InfoCard(page, 0f, 250f, UiStyle.Sky, "Die Ringwelt", "Ein schmales Meeresband, das sich vor und hinter dir in den Himmel wölbt. Deine Insel hält von allein Fahrt.");
            InfoCard(page, -270f, 226f, UiStyle.Cream, "Der Rand", "Am Rand des Bands endet die Welt. Hinausfahren kannst du nicht: Dort schäumt das Wasser und schiebt dich sanft wieder auf die Bahn.");
            var time = InfoCard(page, -516f, 240f, UiStyle.Coral, "Wie weit kommst du?", "Deine Insel sinkt. Versinkt sie, ist der Lauf vorbei – deine weiteste Strecke bleibt als Rekord.");
            SampleBar(time, -176f, 0.24f, UiStyle.Coral, "sinkt – Treibgut holen!");
            return page.gameObject;
        }

        GameObject BuildAdventureControls(RectTransform panel)
        {
            var page = NewPage(panel, "PageAdventureControls");

            var desktop = _desktopCards[1] = UiStyle.Card(page, "Desktop", new Vector2(ContentWidth, 396f)).TopCenter(Vector2.zero, new Vector2(ContentWidth, 396f));
            CardHeading(desktop, "Am Computer");
            KeyRow(desktop, -92f, "seitlich lenken", "A", "D");
            KeyRow(desktop, -166f, "bremsen", "S");
            KeyRow(desktop, -240f, "oder Mausrad: Zoom", "Q", "E");
            KeyRow(desktop, -314f, "Pause", "Esc");

            var touch = _touchCards[1] = UiStyle.Card(page, "Touch", new Vector2(ContentWidth, 322f)).TopCenter(new Vector2(0f, -416f), new Vector2(ContentWidth, 322f));
            CardHeading(touch, "Am Handy");
            StickGlyph(GlyphRow(touch, -96f, ModeTexts.SteerHint(GameMode.Adventure, true, true)));
            var ahead = GlyphRow(touch, -200f, "Deine Insel fährt von allein");
            var arrow = UiStyle.Shape(ahead, "Arrow", UiSprites.Arrow, UiStyle.Cream);
            arrow.rectTransform.Center(Vector2.zero, new Vector2(56f, 56f));

            InfoCard(page, -758f, 190f, UiStyle.Mint, "Schwung", "Treibgut und Surfen geben Schwung – du wirst schneller. Ein Rempler halbiert ihn.", 30);
            return page.gameObject;
        }

        GameObject BuildAdventureRoute(RectTransform panel)
        {
            var page = NewPage(panel, "PageAdventureRoute");
            InfoCard(page, 0f, 206f, UiStyle.Sand, "Inseln sind Hindernisse", "Hier wird nicht gerammt: Jede Insel wirft dich zurück und kostet Auftrieb. Fahr außen herum!");
            InfoCard(page, -226f, 206f, UiStyle.Mint, "Treibgut hebt dich", "Kisten, Fässer und Flaschen auf der Bahn geben Auftrieb – das Einzige, was dich wieder hochbringt.");
            InfoCard(page, -452f, 250f, UiStyle.Sky, "Surfspuren", "Die Plattengrenzen laufen längs der Bahn und wandern. Fahr an ihnen entlang – dort bist du am schnellsten.");
            InfoCard(page, -722f, 206f, UiStyle.Coral, "Wale", "Fährst du über einen Wal, schiebt er dich an – und du gleitest kurz durch alle Hindernisse.");
            return page.gameObject;
        }

        static void Cross(Transform parent, float size)
        {
            for (int i = 0; i < 2; i++)
            {
                var bar = UiStyle.Pill(parent, "Cross" + i, new Vector2(size, 8f), UiStyle.Cream);
                bar.rectTransform.Center(Vector2.zero, new Vector2(size, 8f));
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            }
        }
    }
}
