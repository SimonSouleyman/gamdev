using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using Drift.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Cozy mode only: little bubbles over the islands that give a drive its destination. "Neue Art" (a species the
    // journal has not seen yet lives there), "Ereignis" (a herd shows its signature move, a fire, a village
    // celebrating, a volcano rising) and "Vulkan". Islands off screen get a soft arrow at the screen edge instead,
    // for the nearest few only. Passive: nothing here takes input. The facts are scanned a few times a second and
    // cached per island; each frame only projects the handful of shown widgets.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class IslandHints : MonoBehaviour
    {
        const string CanvasName = "IslandHintsCanvas";
        const float BubbleSize = 96f, EdgeSize = 76f;

        public Island player;
        public GameSession session;
        public SaveManager saveManager;
        public WatchTools watch;
        public TutorialGuide tutorial;

        [Header("Suche")]
        [Tooltip("So oft pro Sekunde werden die Inseln nach Hinweisen abgesucht.")]
        [Range(0.5f, 10f)] public float scansPerSecond = 2.5f;
        [Tooltip("Inseln, deren Rand weiter als das vom Spieler entfernt ist, bekommen keinen Hinweis.")]
        public float scanRange = 170f;
        [Tooltip("Pflanzenlisten sind teuer: jede Insel wird höchstens so oft (Sekunden) neu gezählt.")]
        public float plantRefresh = 2.5f;
        [Tooltip("So viele Inseln zählen ihre Pflanzen pro Suche neu.")]
        public int plantRefreshPerScan = 4;
        [Tooltip("So lange (Sekunden) bleibt ein Schauspiel einer Herde als Ereignis angezeigt.")]
        public float showHold = 14f;

        [Header("Anzeige")]
        [Tooltip("Höchstens so viele Blasen über sichtbaren Inseln.")]
        [Range(0, 8)] public int maxBubbles = 4;
        [Tooltip("Höchstens so viele Pfeile am Bildschirmrand für Inseln außerhalb des Bildes.")]
        [Range(0, 3)] public int maxEdge = 3;
        [Tooltip("Blasen nur bis zu dieser Entfernung (Inselrand).")]
        public float bubbleRange = 150f;
        [Tooltip("Randpfeile nur bis zu dieser Entfernung (Inselrand).")]
        public float edgeRange = 120f;
        [Tooltip("So hoch über dem höchsten Punkt der Insel schwebt die Blase.")]
        public float lift = 2.2f;
        [Tooltip("Abstand der Randpfeile vom Bildschirmrand (Canvas-Einheiten): links/rechts, unten, oben (HUD frei lassen).")]
        public Vector3 edgeMargins = new Vector3(110f, 420f, 330f);
        public float fadeSeconds = 0.35f;
        public int sortingOrder = 2;
        [Tooltip("Nur im Editor: zeigt Beispiel-Hinweise auf den Inseln der Szene (Screenshots).")]
        public bool editorPreview;

        sealed class Entry
        {
            public Island island;
            public IslandHerdSystem herds;
            public IslandLifeSystem life;
            public IslandCrittersSystem critters;
            public IslandSettlementSystem settlement;
            public int stamp, version = int.MinValue;
            public float top, plantsNext, showUntil, fireUntil, distance;
            public ulong plants;
            public int lastSig = -1, lastIgn = -1;
            public LifeKind showKind;
            public IslandHintKind kind;
            public IslandHintEvent ev;
            public int species = -1;
            public Vector3 viewport;
            public Vector2 pos, dir;

            public void Reset(Island i)
            {
                island = i;
                herds = i.GetComponent<IslandHerdSystem>();
                life = i.GetComponent<IslandLifeSystem>();
                critters = i.GetComponent<IslandCrittersSystem>();
                settlement = i.GetComponent<IslandSettlementSystem>();
                version = int.MinValue;
                top = plantsNext = showUntil = fireUntil = 0f;
                plants = 0;
                lastSig = lastIgn = -1;
                kind = IslandHintKind.None;
                ev = IslandHintEvent.None;
                species = -1;
            }
        }

        sealed class Widget
        {
            public RectTransform rect, pointer;
            public CanvasGroup group;
            public Image ring, disc, glyph, badge, sparkle;
            public Text badgeText, label;
            public RectTransform labelPill;
            public Island island;
            public bool wanted, edge;
            public float alpha;
            public IslandHintKind kind = (IslandHintKind)(-1);
            public IslandHintEvent ev;
            public int species = -2;
            public LifeKind showKind;
        }

        Canvas _canvas;
        RectTransform _root;
        readonly List<Entry> _entries = new();
        readonly Stack<Entry> _spare = new();
        readonly Dictionary<Island, Entry> _byIsland = new();
        Widget[] _bubbles = new Widget[0], _edges = new Widget[0];
        float[] _rank = new float[32];
        bool[] _bubbleOk = new bool[32], _edgeOk = new bool[32];
        int[] _bubblePick = new int[8], _edgePick = new int[3], _scratch = new int[12];
        Vector2[] _pos = new Vector2[32];
        float _scanTimer, _lookupTimer;
        int _stamp;
        bool _shown;

        public int HintCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Count; i++) if (_entries[i].kind != IslandHintKind.None) n++;
                return n;
            }
        }
        public bool Shown => _shown;

        public IslandHintKind KindOf(Island island) => island != null && _byIsland.TryGetValue(island, out var e) ? e.kind : IslandHintKind.None;
        public IslandHintEvent EventOf(Island island) => island != null && _byIsland.TryGetValue(island, out var e) ? e.ev : IslandHintEvent.None;
        public int SpeciesOf(Island island) => island != null && _byIsland.TryGetValue(island, out var e) ? e.species : -1;

        public int VisibleBubbles => CountVisible(_bubbles);
        public int VisibleEdges => CountVisible(_edges);

        static int CountVisible(Widget[] ws)
        {
            int n = 0;
            foreach (var w in ws) if (w.rect.gameObject.activeSelf && w.alpha > 0.01f) n++;
            return n;
        }

        void OnEnable()
        {
            Build();
            _scanTimer = 0f;
            _lookupTimer = 0f;
        }

        void OnDisable()
        {
            for (int i = 0; i < _entries.Count; i++) _spare.Push(_entries[i]);
            _entries.Clear();
            _byIsland.Clear();
        }

        void Resolve()
        {
            if (player == null)
                for (int i = 0; i < Island.All.Count; i++)
                {
                    var isl = Island.All[i];
                    if (isl != null && isl.useKeyboardInput) { player = isl; break; }
                }
            if (session != null && saveManager != null && watch != null && tutorial != null && streamer != null) return;
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer > 0f) return;
            _lookupTimer = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            if (watch == null) watch = FindAnyObjectByType<WatchTools>();
            if (tutorial == null) tutorial = FindAnyObjectByType<TutorialGuide>();
            if (streamer == null) streamer = FindAnyObjectByType<WorldStreamer>();
        }

        bool ShouldShow()
        {
            // Before the edit-mode branch: an adventure frame shows no hints in the Game view either.
            if (!WatchRules.Allowed(WatchFeature.IslandHint)) return false;
            if (!Application.isPlaying) return editorPreview;
            if (WatchTools.HudHidden) return false;
            if (session != null && session.Current != GameSession.State.Playing) return false;
            if (watch != null && (watch.JournalOpen || watch.AlbumOpen || watch.Following || watch.PhotoActive)) return false;
            return tutorial == null || !tutorial.BubbleVisible;
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            Resolve();
            bool show = ShouldShow() && player != null;
            if (show != _shown)
            {
                _shown = show;
                if (!show) HideAll();
            }
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;

            _scanTimer -= Time.unscaledDeltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 1f / Mathf.Max(0.1f, scansPerSecond);
                Scan(Application.isPlaying ? Time.time : 0f);
            }
            Present(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        // Scans and draws at once (edit-mode screenshots, tests).
        public void RefreshNow()
        {
            if (_canvas == null) Build();
            Resolve();
            _shown = ShouldShow() && player != null;
            _canvas.enabled = _shown;
            if (!_shown) return;
            Scan(Application.isPlaying ? Time.time : 0f);
            Present(10f);
        }

        // ---------------------------------------------------------------- scan

        WorldStreamer streamer;
        Island _lastIsland;
        // The last island's planned spot while it is not streamed in (it can lie beyond the loaded chunks).
        bool _lastFar;
        Vector2 _lastFarPos;
        Widget _farLast;
        static readonly Entry FarLastEntry = new Entry { kind = IslandHintKind.LastIsland };

        // Cozy with exactly one planned island left (owner, v0.6.7: "eine Anzeige, um sie einfacher zu finden"): the
        // nearest non-volcano island that is not the player's. Volcanoes are bonus land and never count.
        Island FindLastIsland()
        {
            if (!Application.isPlaying || streamer == null || GameModes.Current != GameMode.Cozy) return null;
            streamer.Progress(out _, out int left);
            _lastFar = false;
            if (left != 1) return null;
            Island best = null;
            float bestD = float.MaxValue;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || isl == player || isl.useKeyboardInput || isl.isVolcano || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                float d = (isl.PlanarPosition - player.PlanarPosition).sqrMagnitude;
                if (d < bestD) { bestD = d; best = isl; }
            }
            // Not streamed in: point at its planned spot, the nearest copy on the wrapped world.
            if (best == null)
            {
                var slots = streamer.WorldSlots();
                float size = streamer.WorldSize;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].consumed) continue;
                    Vector2 d = slots[i].pos - player.PlanarPosition;
                    if (size > 0f) d -= size * new Vector2(Mathf.Round(d.x / size), Mathf.Round(d.y / size));
                    _lastFarPos = player.PlanarPosition + d;
                    _lastFar = true;
                    break;
                }
            }
            return best;
        }

        void PresentFarLast(Camera cam, Vector2 centre, Rect inner, Vector2 pixels, float step, float time)
        {
            var w = _farLast;
            if (w == null) return;
            bool want = _lastFar && cam != null;
            w.alpha = Mathf.MoveTowards(w.alpha, want ? 1f : 0f, step);
            if (w.alpha <= 0f) { if (w.rect.gameObject.activeSelf) w.rect.gameObject.SetActive(false); return; }
            if (!w.rect.gameObject.activeSelf) w.rect.gameObject.SetActive(true);
            w.group.alpha = w.alpha * w.alpha * (3f - 2f * w.alpha);
            Style(w, FarLastEntry);
            if (!want) return;
            Vector3 vp = cam.WorldToViewportPoint(CurvedWorld.Bend(new Vector3(_lastFarPos.x, 0f, _lastFarPos.y)));
            Vector2 dir = IslandHintLogic.DirectionOf(vp, pixels);
            w.rect.anchoredPosition = IslandHintLogic.EdgePoint(centre, dir, inner);
            w.pointer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            float pulse = 1f + 0.05f * Mathf.Sin(time * 3f);
            w.rect.localScale = new Vector3(pulse, pulse, 1f);
            if (w.sparkle.enabled) w.sparkle.color = UiStyle.WithAlpha(w.sparkle.color, 0.5f + 0.5f * Mathf.Sin(time * 4.2f));
        }

        void Scan(float now)
        {
            _stamp++;
            _lastIsland = FindLastIsland();
            var journal = saveManager != null ? saveManager.Journal : null;
            bool preview = !Application.isPlaying && editorPreview;
            ulong unseen = preview ? CollectionCatalog.CollectibleMask
                : journal != null ? journal.UnseenLifeMask & CollectionCatalog.CollectibleMask : 0UL;
            Vector2 origin = player.PlanarPosition;
            int plantBudget = plantRefreshPerScan;
            var islands = Island.All;
            for (int n = 0; n < islands.Count; n++)
            {
                var island = islands[n];
                if (island == null || island == player || island.useKeyboardInput || !island.isActiveAndEnabled || island.IsSunk) continue;
                float dist = (island.PlanarPosition - origin).magnitude - island.BoundingRadius;
                if (dist > scanRange && island != _lastIsland) continue;
                if (!_byIsland.TryGetValue(island, out var e))
                {
                    e = _spare.Count > 0 ? _spare.Pop() : new Entry();
                    e.Reset(island);
                    _byIsland.Add(island, e);
                    _entries.Add(e);
                }
                e.stamp = _stamp;
                e.distance = dist;
                if (e.version != island.Version)
                {
                    e.version = island.Version;
                    e.top = island.MaxHeight;
                }

                var facts = new IslandHintFacts
                {
                    volcano = island.isVolcano,
                    emerging = island.IsEmerging,
                    festival = e.settlement != null && e.settlement.Celebrating,
                };
                if (e.herds != null)
                {
                    if ((unseen & CollectionCatalog.AnimalMask) != 0) facts.present |= e.herds.SpeciesPresent;
                    float before = e.showUntil;
                    e.showUntil = IslandHintLogic.Hold(ref e.lastSig, e.herds.SignatureMoves, now, e.showUntil, showHold);
                    if (e.showUntil != before) e.showKind = Performer(e.herds);
                }
                if (e.life != null)
                {
                    if ((unseen & CollectionCatalog.PlantMask) != 0)
                    {
                        if (now >= e.plantsNext && plantBudget > 0 || e.plantsNext == 0f || preview)
                        {
                            plantBudget--;
                            e.plants = e.life.PlantsPresent;
                            e.plantsNext = now + plantRefresh + 0.01f;
                        }
                        facts.present |= e.plants;
                    }
                    e.fireUntil = IslandHintLogic.Hold(ref e.lastIgn, e.life.IgnitionCount, now, e.fireUntil, e.life.fireDuration);
                }
                if (e.critters != null && (unseen & CollectionCatalog.CritterMask) != 0) facts.present |= VisibleCritters(e.critters);
                facts.show = now < e.showUntil;
                facts.fire = now < e.fireUntil;
                if (preview && island.isVolcano) facts.present = 0;
                else if (preview && (n % 3) == 2)
                {
                    int cycle = (n / 3) % 3;
                    facts.present = 0;
                    facts.fire = cycle == 0;
                    facts.show = cycle == 1;
                    facts.festival = cycle == 2;
                    e.showKind = LifeKind.Sheep;
                }
                e.kind = IslandHintLogic.Classify(facts, unseen, out e.ev, out e.species);
                if (island == _lastIsland) e.kind = IslandHintKind.LastIsland;
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.stamp == _stamp && e.island != null) continue;
                _byIsland.Remove(e.island);
                _entries.RemoveAt(i);
                e.island = null;
                _spare.Push(e);
            }
        }

        // Mirrors WatchTools.CritterVisible: what the journal would count as seen.
        static ulong VisibleCritters(IslandCrittersSystem critters)
        {
            ulong mask = 0;
            int cn = critters.CritterCount;
            for (int i = 0; i < cn; i++)
            {
                if (critters.DyingOf(i) || critters.FadeOf(i) < 0.5f || critters.StateOf(i) == CritterState.Hidden) continue;
                mask |= 1UL << (int)critters.KindOf(i);
            }
            return mask;
        }

        static LifeKind Performer(IslandHerdSystem herds)
        {
            int hn = herds.HerdCount;
            for (int h = 0; h < hn; h++)
            {
                int m = herds.HerdSize(h);
                for (int k = 0; k < m; k++)
                    if (IslandHerdSystem.IsSignature(herds.ActivityOf(h, k))) return herds.HerdKind(h);
            }
            return hn > 0 ? herds.HerdKind(0) : LifeKind.Sheep;
        }

        // ---------------------------------------------------------------- present

        void Present(float dt)
        {
            var cam = Camera.main;
            int count = _entries.Count;
            if (_rank.Length < count)
            {
                int cap = Mathf.NextPowerOfTwo(count);
                _rank = new float[cap];
                _bubbleOk = new bool[cap];
                _edgeOk = new bool[cap];
                _pos = new Vector2[cap];
            }

            var rect = _root.rect;
            var inner = new Rect(rect.xMin + edgeMargins.x, rect.yMin + edgeMargins.y,
                Mathf.Max(0f, rect.width - 2f * edgeMargins.x), Mathf.Max(0f, rect.height - edgeMargins.y - edgeMargins.z));
            Vector2 centre = Vector2.zero, pixels = Vector2.one;
            if (cam != null)
            {
                pixels = new Vector2(cam.pixelWidth, cam.pixelHeight);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, pixels * 0.5f, UiCamera, out centre);
                centre = new Vector2(Mathf.Clamp(centre.x, inner.xMin, inner.xMax), Mathf.Clamp(centre.y, inner.yMin, inner.yMax));
            }
            float mx = 0.5f * edgeMargins.x / Mathf.Max(1f, rect.width), my = 0.04f;
            // The top band belongs to the HUD panel: an island projected there gets an edge pointer under it, not a
            // bubble on top of the text.
            float myTop = Mathf.Max(my, edgeMargins.z / Mathf.Max(1f, rect.height));
            for (int i = 0; i < count; i++)
            {
                var e = _entries[i];
                _bubbleOk[i] = _edgeOk[i] = false;
                if (e.kind == IslandHintKind.None || e.island == null || cam == null) continue;
                Vector3 anchor = e.island.transform.position + Vector3.up * (e.top + lift);
                e.viewport = cam.WorldToViewportPoint(CurvedWorld.Bend(anchor));
                _rank[i] = IslandHintLogic.Rank(e.kind, e.distance);
                if (IslandHintLogic.OnScreen(e.viewport, mx, my, myTop))
                {
                    _bubbleOk[i] = e.distance <= bubbleRange || e.kind == IslandHintKind.LastIsland;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, new Vector2(e.viewport.x * pixels.x, e.viewport.y * pixels.y), UiCamera, out e.pos);
                }
                else
                {
                    _edgeOk[i] = e.distance <= edgeRange || e.kind == IslandHintKind.LastIsland;
                    e.dir = IslandHintLogic.DirectionOf(e.viewport, pixels);
                    e.pos = IslandHintLogic.EdgePoint(centre, e.dir, inner);
                }
                // A bubble stacks label, disc and tail: squashing y makes two bubbles need about twice the room above
                // each other that they need side by side.
                _pos[i] = _bubbleOk[i] ? new Vector2(e.pos.x, e.pos.y * 0.55f) : e.pos;
            }
            int nb = SelectInto(_bubblePick, Mathf.Min(maxBubbles, _bubbles.Length), count, _bubbleOk, BubbleSize * 1.2f);
            int ne = SelectInto(_edgePick, Mathf.Min(maxEdge, _edges.Length), count, _edgeOk, EdgeSize * 1.5f);
            Assign(_bubbles, _bubblePick, nb);
            Assign(_edges, _edgePick, ne);

            float step = fadeSeconds > 0f ? dt / fadeSeconds : 1f;
            float t = Application.isPlaying ? Time.unscaledTime : 0.6f;
            foreach (var w in _bubbles) Tick(w, step, t);
            foreach (var w in _edges) Tick(w, step, t);
            PresentFarLast(cam, centre, inner, pixels, step, t);
        }

        int SelectInto(int[] pick, int n, int count, bool[] ok, float spacing)
        {
            if (n <= 0) return 0;
            int got = IslandHintLogic.SelectSpread(_rank, ok, _pos, count, spacing, _scratch, pick);
            return Mathf.Min(got, n);
        }

        void Assign(Widget[] pool, int[] pick, int n)
        {
            foreach (var w in pool) w.wanted = false;
            for (int k = 0; k < n; k++)
            {
                var island = _entries[pick[k]].island;
                Widget have = null;
                foreach (var w in pool) if (w.island == island) { have = w; break; }
                if (have != null) { have.wanted = true; continue; }
                Widget free = null;
                foreach (var w in pool)
                {
                    if (w.wanted) continue;
                    if (w.island == null) { free = w; break; }
                    if (free == null || w.alpha < free.alpha) free = w;
                }
                if (free == null) continue;
                // A widget still showing another island blinks over; fine for the rare steal.
                free.island = island;
                free.alpha = 0f;
                free.wanted = true;
            }
        }

        void Tick(Widget w, float step, float time)
        {
            if (w.island == null)
            {
                // Merged or unloaded: gone at once.
                w.alpha = 0f;
                w.wanted = false;
                if (w.rect.gameObject.activeSelf) w.rect.gameObject.SetActive(false);
                return;
            }
            w.alpha = Mathf.MoveTowards(w.alpha, w.wanted ? 1f : 0f, step);
            if (!w.wanted && w.alpha <= 0f)
            {
                w.island = null;
                if (w.rect.gameObject.activeSelf) w.rect.gameObject.SetActive(false);
                return;
            }
            if (!w.rect.gameObject.activeSelf) w.rect.gameObject.SetActive(true);
            w.group.alpha = w.alpha * w.alpha * (3f - 2f * w.alpha);
            // Out of range or no longer interesting: fade out where it stands.
            if (!_byIsland.TryGetValue(w.island, out var e) || e.kind == IslandHintKind.None) return;
            Style(w, e);

            if (!w.edge)
            {
                if (e.viewport.z <= 0f) return;
                float bob = Mathf.Sin(time * 2.1f + (w.island.shapeSeed % 97) * 0.37f) * 6f;
                w.rect.anchoredPosition = e.pos + new Vector2(0f, BubbleSize * 0.62f + bob);
                float s = Mathf.Lerp(1.08f, 0.78f, Mathf.Clamp01(e.distance / Mathf.Max(1f, bubbleRange)));
                w.rect.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                w.rect.anchoredPosition = e.pos;
                w.pointer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(e.dir.y, e.dir.x) * Mathf.Rad2Deg);
                float pulse = 1f + 0.05f * Mathf.Sin(time * 3f);
                w.rect.localScale = new Vector3(pulse, pulse, 1f);
            }
            if (w.sparkle.enabled)
            {
                float tw = 0.5f + 0.5f * Mathf.Sin(time * 4.2f);
                w.sparkle.rectTransform.localScale = Vector3.one * (0.75f + 0.35f * tw);
                w.sparkle.color = UiStyle.WithAlpha(UiStyle.Sand, 0.55f + 0.45f * tw);
            }
        }

        // Null for the overlay canvas the game uses; a verification render may switch it to a camera canvas.
        Camera UiCamera => _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        static Color RingOf(IslandHintKind kind) =>
            kind == IslandHintKind.LastIsland ? UiStyle.Mint : kind == IslandHintKind.NewSpecies ? UiStyle.Sand : kind == IslandHintKind.Event ? UiStyle.Coral : UiStyle.Lagoon;

        void Style(Widget w, Entry e)
        {
            if (w.kind == e.kind && w.ev == e.ev && w.species == e.species && w.showKind == e.showKind) return;
            w.kind = e.kind;
            w.ev = e.ev;
            w.species = e.species;
            w.showKind = e.showKind;
            Color ring = RingOf(e.kind);
            w.ring.color = ring;
            if (w.pointer != null) w.pointer.GetComponent<Image>().color = ring;
            bool volcano = e.kind == IslandHintKind.Volcano;
            w.disc.color = volcano ? UiStyle.GlassDense : UiStyle.Cream;
            w.glyph.color = volcano ? UiStyle.Cream : UiStyle.Ink;
            w.glyph.sprite = GlyphOf(e);
            w.badge.enabled = w.badgeText.enabled = !volcano;
            w.badge.color = ring;
            w.badgeText.text = e.kind == IslandHintKind.NewSpecies ? "?" : e.kind == IslandHintKind.LastIsland ? "1" : "!";
            w.sparkle.enabled = e.kind == IslandHintKind.NewSpecies || e.kind == IslandHintKind.LastIsland;
            if (w.label != null)
            {
                w.label.text = IslandHintLogic.LabelOf(e.kind, e.ev);
                w.labelPill.sizeDelta = new Vector2(Mathf.Max(96f, w.label.preferredWidth + 34f), 44f);
            }
        }

        static Sprite GlyphOf(Entry e)
        {
            switch (e.kind)
            {
                case IslandHintKind.NewSpecies:
                    return JournalGlyphs.Of(JournalGlyphs.For(CollectionCatalog.At(e.species)));
                case IslandHintKind.Volcano:
                    return HintGlyphs.Of(HintGlyph.Volcano);
                case IslandHintKind.LastIsland:
                    return HintGlyphs.Of(HintGlyph.Island);
                default:
                    switch (e.ev)
                    {
                        case IslandHintEvent.Show:
                            int idx = CollectionCatalog.IndexOf(e.showKind);
                            return JournalGlyphs.Of(idx >= 0 ? JournalGlyphs.For(CollectionCatalog.At(idx)) : JournalGlyph.Animal);
                        case IslandHintEvent.Fire: return HintGlyphs.Of(HintGlyph.Flame);
                        case IslandHintEvent.Festival: return HintGlyphs.Of(HintGlyph.Party);
                        default: return HintGlyphs.Of(HintGlyph.Volcano);
                    }
            }
        }

        void HideAll()
        {
            foreach (var w in _bubbles) Clear(w);
            foreach (var w in _edges) Clear(w);
            if (_farLast != null) Clear(_farLast);
        }

        static void Clear(Widget w)
        {
            w.island = null;
            w.wanted = false;
            w.alpha = 0f;
            if (w.rect != null && w.rect.gameObject.activeSelf) w.rect.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, false, out _root);
            _bubbles = new Widget[8];
            for (int i = 0; i < _bubbles.Length; i++) _bubbles[i] = BuildWidget(false, i);
            _farLast = BuildWidget(true, 9);
            _edges = new Widget[3];
            for (int i = 0; i < _edges.Length; i++) _edges[i] = BuildWidget(true, i);
            _shown = false;
            _canvas.enabled = false;
        }

        Widget BuildWidget(bool edge, int index)
        {
            float size = edge ? EdgeSize : BubbleSize;
            var rect = UiStyle.Rect(_root, (edge ? "Edge" : "Bubble") + index);
            rect.Center(Vector2.zero, new Vector2(size, size));
            var w = new Widget { rect = rect, edge = edge, group = rect.gameObject.AddComponent<CanvasGroup>() };
            w.group.blocksRaycasts = false;
            w.group.interactable = false;

            var shadow = UiStyle.Shape(rect, "Shadow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Shadow, 0.5f));
            shadow.rectTransform.Center(new Vector2(0f, -8f), new Vector2(size * 1.35f, size * 1.35f));
            if (edge)
            {
                var p = UiStyle.Shape(rect, "Pointer", HintGlyphs.Of(HintGlyph.Pointer), UiStyle.Cream);
                w.pointer = p.rectTransform;
                w.pointer.Center(Vector2.zero, new Vector2(size * 1.6f, size * 1.6f));
                // The sprite's tip sits at +0.9 of its half size: the arrow pokes out of the ring towards the island.
                p.preserveAspect = true;
            }
            else
            {
                var tail = UiStyle.Dot(rect, "Tail", 18f, UiStyle.CreamSoft);
                tail.rectTransform.Center(new Vector2(0f, -size * 0.62f), new Vector2(18f, 18f));
                var tail2 = UiStyle.Dot(rect, "Tail2", 10f, UiStyle.CreamSoft);
                tail2.rectTransform.Center(new Vector2(0f, -size * 0.62f - 22f), new Vector2(10f, 10f));
            }
            w.ring = UiStyle.Dot(rect, "Ring", size, UiStyle.Sand);
            w.ring.rectTransform.Center(Vector2.zero, new Vector2(size, size));
            w.disc = UiStyle.Dot(rect, "Disc", size - 12f, UiStyle.Cream);
            w.disc.rectTransform.Center(Vector2.zero, new Vector2(size - 12f, size - 12f));
            w.glyph = UiStyle.Shape(rect, "Glyph", JournalGlyphs.Of(JournalGlyph.Animal), UiStyle.Ink);
            w.glyph.rectTransform.Center(Vector2.zero, new Vector2(size * 0.62f, size * 0.62f));

            float bs = edge ? 30f : 38f;
            w.badge = UiStyle.Dot(rect, "Badge", bs, UiStyle.Sand);
            w.badge.rectTransform.Center(new Vector2(size * 0.36f, size * 0.36f), new Vector2(bs, bs));
            w.badgeText = UiStyle.Label(w.badge.rectTransform, "?", edge ? 24 : 30, UiStyle.Ink, TextAnchor.MiddleCenter, true);
            w.badgeText.rectTransform.Stretch();

            w.sparkle = UiStyle.Shape(rect, "Sparkle", HintGlyphs.Of(HintGlyph.Sparkle), UiStyle.Sand);
            float ss = edge ? 26f : 36f;
            w.sparkle.rectTransform.Center(new Vector2(-size * 0.42f, size * 0.4f), new Vector2(ss, ss));

            if (!edge)
            {
                var pill = UiStyle.Pill(rect, "LabelPill", new Vector2(160f, 44f), UiStyle.Glass);
                w.labelPill = pill.rectTransform;
                w.labelPill.Center(new Vector2(0f, size * 0.5f + 34f), new Vector2(160f, 44f));
                w.label = UiStyle.Label(w.labelPill, "", UiStyle.Caption, UiStyle.Cream, TextAnchor.MiddleCenter, true);
                w.label.rectTransform.Stretch();
            }
            rect.gameObject.SetActive(false);
            return w;
        }
    }
}
