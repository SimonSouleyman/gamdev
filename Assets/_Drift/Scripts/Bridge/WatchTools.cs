using System;
using System.Collections;
using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.UI;
using Drift.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Drift.Bridge
{
    // Watch tools (Phase 7): tap-an-animal popup, follow-a-herd camera, discovery journal, photo mode and album.
    // Everything here only reads the simulation; the only world state it touches is the camera and, while photo
    // mode or herd following runs, the session's input and sink holds (GameSession.Photo*/Follow*Hold).
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WatchTools : MonoBehaviour
    {
        public enum EditorPreview { None, Popup, Journal, Photo, Album, AlbumPhoto }

        const string CanvasName = "WatchToolsCanvas";
        const float ToastSeconds = 3f;
        // Lower edge of "Zurück zur Insel" + herd chip, measured from the top of the safe area.
        const float FollowUiBottom = 640f;

        public GameSession session;
        public Island player;
        public IslandChaseCamera chaseCamera;
        public TouchControls touch;
        public SaveManager saveManager;
        public FlockSystem flocks;
        public FishSystem fish;
        public SeaLifeSystem seaLife;
        public ShipSystem ships;
        // Keeps the permanent "Lebensbuch" (kinds ever collected, one PlayerPrefs string) up to date.
        public bool lifeBook = true;
        public EditorPreview editorPreview = EditorPreview.None;
        public int sortingOrder = 15;
        // Logs every press: what it hit or why it was not a tap.
        public bool debugTaps;
        // Tap thresholds in canvas units, turned into pixels by TapPicker.PixelsPerUnit (1 unit = 1 px on any
        // 1080p screen). The fields were renamed when their defaults grew so stale scene values do not survive.
        public float pickRadius = 64f;
        public float tapSlop = 30f;
        public float tapHoldSeconds = 0.6f;
        // Islands whose edge is farther than this from both the camera and its focus are not searched.
        public float tapRange = 60f;
        public float popupSeconds = 4f;
        public float popupFadeSeconds = 0.6f;
        // Following a herd starts from (and R returns to) the chase framing at this zoom level.
        public float followZoomLevel = 0.3f;
        public float discoverRange = 25f;
        public float discoverInterval = 1f;
        public float journalRefresh = 0.5f;
        public float orbitDegPerPixel = 0.2f;
        public float photoMinDistance = 1.5f;
        public float photoMinHeight = 0.4f;
        public float photoGroundClearance = 0.6f;
        // The photo pivot may be panned this many bounding radii away from the island centre.
        public float panRadiusFactor = 1.5f;
        public float returnEaseSeconds = 0.6f;

        static readonly string[] Moods = { "grast", "schaut auf", "wandert", "ruht", "schläft", "spielt" };
        static readonly string[] CritterMoods = { "schaut sich um", "ist unterwegs", "gräbt sich ein", "versteckt sich", "ruht", "winkt mit den Scheren", "gräbt ein Nest", "tanzt im Spiralflug" };

        public static bool HudHidden { get; private set; }

        Canvas _canvas;
        RectTransform _root;

        GameObject _popup, _popupFollow;
        CanvasGroup _popupGroup, _markerGroup;
        RectTransform _popupRect, _marker, _ripple;
        Image _rippleImage;
        float _rippleT = 1f;
        Text _popupTitle, _popupHerd, _popupMood, _popupOrigin;
        RectTransform _popupFollowRect;
        IslandHerdSystem _popupHerds;
        IslandCrittersSystem _popupCritters;
        Island _popupIsland;
        int _popupHerdIndex = -1, _popupMember = -1;
        LifeKind _popupKind;
        float _popupTimer;
        int _shownState = -1, _shownSize = -1, _shownYoung = -1;
        bool _popupForeign;

        GameObject _followButton, _followChip;
        Text _followChipText;
        IslandHerdSystem _followHerds;
        // Anything else being watched (a plant, a critter, a flock, a whale, a boat); null while a herd is followed.
        WatchSubject _observe;
        Island _followIsland;
        int _followHerd = -1, _followHerdCount;
        LifeKind _followKind;
        Vector2 _followCenter;
        Vector3 _followFocus;
        float _followRadius = 1.5f, _chipTimer;
        int _chipSize = -1, _chipMood = -1;
        readonly int[] _moodCount = new int[6];

        GameObject _journal;
        readonly JournalPanel _journalPanel = new();
        bool _journalOpen, _journalPausedSession;
        int _journalVersion = -1;
        float _journalTimer;
        DiscoveryJournal _sampleJournal;
        readonly CollectionToasts _toasts = new();
        readonly LifeBook _lifeBook = new();
        bool _lifeBookLoaded;
        GameObject _newsChip;
        RectTransform _newsRect;
        Text _newsText;
        Image _newsIcon;
        int _plantCursor;

        GameObject _photo, _bookButton, _cameraButton, _toastChip;
        Text _photoToast;
        Image _flash;
        float _toastTimer, _flashT;
        bool _photoActive, _capturing;
        Coroutine _captureRoutine;
        readonly List<Canvas> _hiddenCanvases = new();
        readonly PhotoAlbum _album = new();
        EditorPreview _albumPreview = EditorPreview.None;
        // The orbit camera of photo mode and herd following: while _driving, the chase camera is suspended and
        // LateUpdate poses Camera.main from the rig around the subject (followed herd, else the player island).
        readonly PhotoRig _rig = new();
        bool _driving;
        float _zoomBeforeDrive = 1f, _rotBlend = 1f;
        Quaternion _rotBlendFrom = Quaternion.identity;
        Vector3 _lastSubject;
        OrbitInput _input;
        GameObject _hintChip;
        RectTransform _hintRect;
        Text _hintText;
        int _hintShown = -1;
        bool _dragging, _mouseOrbit, _panning, _pinching;
        int _dragId;
        Vector2 _lastPointer, _lastMouse, _pinchCenter;
        float _pinchDist;

        struct Press
        {
            public bool tracking;
            public int id;
            public Vector2 pos;
            public float time;
            public TapPress flags;
        }

        Press _mousePress, _touchPress;
        int _lastTouchId = int.MinValue;
        Vector2 _lastTapPos;
        float _lastTapTime = -10f;

        float _discoverTimer, _lookupTimer;
        readonly Dictionary<IslandHerdSystem, int> _birthsSeen = new();
        readonly Dictionary<IslandLifeSystem, int> _firesSeen = new();

        public bool PhotoActive => _photoActive;
        public bool JournalOpen => _journalOpen;
        public bool Following => _followHerds != null || _observe != null;
        public bool Observing => _observe != null;
        public bool AlbumOpen => _album.IsOpen;
        public bool Capturing => _capturing;
        public PhotoAlbum Album => _album;
        public TapReject LastReject { get; private set; }
        public bool PopupVisible => _popup != null && _popup.activeSelf;
        public Island PopupIsland => _popupIsland;
        public int PopupHerd => _popupHerds != null ? _popupHerdIndex : -1;
        public int PopupMember => _popupMember;
        public LifeKind PopupKind => _popupKind;
        public string PopupTitle => _popupTitle != null ? _popupTitle.text : "";
        public string PopupOrigin => _popupForeign && _popupOrigin != null ? _popupOrigin.text : "";
        public int FollowedHerd => Following ? _followHerd : -1;
        public string FollowChipText => _followChipText != null ? _followChipText.text : "";
        public DiscoveryJournal Journal => saveManager != null ? saveManager.Journal : null;
        public JournalPanel JournalView => _journalPanel;
        public CollectionToasts Toasts => _toasts;
        public string NewsText => _newsText != null ? _newsText.text : "";
        // Kinds ever collected, across all runs (title or album can show LifeBook.Line / Count / Total).
        public LifeBook LifeBook
        {
            get
            {
                if (!_lifeBookLoaded)
                {
                    _lifeBookLoaded = true;
                    _lifeBook.Load();
                }
                return _lifeBook;
            }
        }
        public Vector3 FollowFocus => _followFocus;
        public PhotoRig Rig => _rig;
        public bool CameraDriven => _driving;
        public Vector3 CameraSubject => SubjectPosition();
        public Vector3 CameraPivot => _rig.Pivot(SubjectPosition());
        public string HintText => _hintText != null ? _hintText.text : "";

        void OnEnable()
        {
            if (touch == null) touch = GetComponent<TouchControls>();
            HudHidden = false;
            Build();
            _lookupTimer = 0f;
        }

        void OnDisable()
        {
            if (_photoActive) ExitPhotoMode();
            if (Following) ReturnToIsland();
            _album.Close();
            _albumPreview = EditorPreview.None;
            HudHidden = false;
        }

        // Scene-wide searches are only retried once a second so a legitimately absent reference does not
        // cost a FindAnyObjectByType every frame.
        void Resolve()
        {
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (touch == null) touch = GetComponent<TouchControls>();
            if (session != null && chaseCamera != null && saveManager != null && flocks != null && fish != null && seaLife != null && ships != null) return;
            _lookupTimer -= Time.unscaledDeltaTime;
            if (_lookupTimer > 0f) return;
            _lookupTimer = 1f;
            if (session == null) session = FindAnyObjectByType<GameSession>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<IslandChaseCamera>();
            if (saveManager == null) saveManager = FindAnyObjectByType<SaveManager>();
            if (flocks == null) flocks = FindAnyObjectByType<FlockSystem>();
            if (fish == null) fish = FindAnyObjectByType<FishSystem>();
            if (seaLife == null) seaLife = FindAnyObjectByType<SeaLifeSystem>();
            if (ships == null) ships = FindAnyObjectByType<ShipSystem>();
        }

        void Update()
        {
            if (_canvas == null) return;

            if (!Application.isPlaying)
            {
                EditorPreviewUpdate();
                return;
            }

            Resolve();
            if (session == null) return;
            var s = session.Current;

            if (s != GameSession.State.Playing)
            {
                if (_photoActive) ExitPhotoMode();
                HidePopup();
                CancelPresses();
                if (_journalOpen && s != GameSession.State.Paused) CloseJournal();
                if (Following && s != GameSession.State.Paused) ReturnToIsland();
                if (_album.IsOpen && s == GameSession.State.GameOver) _album.Close();
            }
            else
            {
                if (_album.IsOpen || _journalOpen)
                {
                    CancelPresses();
                    ResetPointers();
                }
                else
                {
                    if (!_photoActive) UpdateTap();
                    if (_driving && !_capturing) GatherInput();
                }
                UpdateFollow();
                UpdatePopup();
                UpdateDiscovery();
            }

            if (_journalOpen) UpdateJournal();
            if (_album.IsOpen) _album.Tick();
            UpdateEffects(Time.unscaledDeltaTime);

            bool playing = s == GameSession.State.Playing;
            UpdateNews(playing && !_photoActive && !_journalOpen && !_album.IsOpen, Time.unscaledDeltaTime);
            bool hudButtons = playing && !_photoActive && !_journalOpen && !_album.IsOpen;
            SetActive(_bookButton, hudButtons);
            SetActive(_cameraButton, hudButtons);
            SetActive(_followButton, playing && Following && !_photoActive);
            SetActive(_followChip, playing && Following && !_photoActive);
            SetActive(_photo, _photoActive && !_album.IsOpen);
            UpdateHint(playing && _driving && !_journalOpen && !_album.IsOpen, _photoActive);
            HudHidden = _photoActive;
        }

        // After the islands and herds have moved this frame, so the camera never trails its subject.
        void LateUpdate()
        {
            if (!_driving || !Application.isPlaying) return;
            StepCamera(_input, Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            _input = default;
            // The island is not steered while a herd is followed; a finger in the stick half orbits instead.
            if (Following && !_photoActive && touch != null && touch.StickActive) touch.Release();
        }

        void UpdateEffects(float dt)
        {
            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;
                if (_toastTimer <= 0f) SetToast("");
            }
            if (_flashT > 0f && _flash != null)
            {
                _flashT = Mathf.Max(0f, _flashT - dt / 0.45f);
                _flash.color = UiStyle.WithAlpha(UiStyle.Cream, 0.85f * _flashT * _flashT);
                SetActive(_flash.gameObject, _flashT > 0f);
            }
            if (_rippleT < 1f && _ripple != null)
            {
                _rippleT = Mathf.Min(1f, _rippleT + dt / 0.45f);
                float e = 1f - (1f - _rippleT) * (1f - _rippleT);
                float k = Mathf.Lerp(0.5f, 2.1f, e);
                _ripple.localScale = new Vector3(k, k, 1f);
                _rippleImage.color = UiStyle.WithAlpha(_rippleImage.color, 0.7f * (1f - _rippleT));
                SetActive(_ripple.gameObject, _rippleT < 1f);
            }
        }

        const float NewsTop = -364f;

        // Collection toasts wait while photo mode, the journal or the album is up and never outlive their run.
        float _noticeTimer;

        void OnNewsTapped()
        {
            var t = _toasts.Current;
            if (_noticeTimer > 0f || !_toasts.Showing || t.count <= 0) return;
            _toasts.Dismiss();
            SetActive(_newsChip, false);
            if (t.count == 1) Watch(t.first);
            else
            {
                OpenJournal();
                ShowJournalTab((int)CollectionCatalog.At(t.first).section);
            }
        }

        void UpdateNews(bool visible, float dt)
        {
            if (_noticeTimer > 0f)
            {
                _noticeTimer -= dt;
                SetActive(_newsChip, visible || Following);
                if (_noticeTimer > 0f) return;
                SetActive(_newsChip, false);
            }
            var state = session != null ? session.Current : GameSession.State.Playing;
            if (state == GameSession.State.Title || state == GameSession.State.GameOver)
            {
                _toasts.Clear();
                SetActive(_newsChip, false);
                return;
            }
            if (!visible)
            {
                SetActive(_newsChip, false);
                return;
            }
            if (_toasts.Tick(dt) && _toasts.Showing) ShowNews(_toasts.Current);
            SetActive(_newsChip, _toasts.Showing);
            if (_toasts.Showing) _newsRect.anchoredPosition = new Vector2(0f, Following ? -FollowUiBottom - 24f : NewsTop);
        }

        Image _newsGo;

        void ShowNews(CollectionToast toast)
        {
            if (_newsText == null) return;
            if (_newsGo != null) _newsGo.gameObject.SetActive(toast.count > 0);
            _newsText.text = toast.text ?? "";
            _newsText.fontSize = toast.strong ? 34 : 30;
            _newsText.color = toast.strong ? UiStyle.Sand : UiStyle.CreamSoft;
            _newsIcon.color = toast.strong ? UiStyle.Sand : UiStyle.Muted;
        }

        void SetToast(string text)
        {
            if (_photoToast != null) _photoToast.text = text;
            SetActive(_toastChip, !string.IsNullOrEmpty(text));
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ---------------------------------------------------------------- tap

        float CanvasScale => _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

        float PixelsPerUnit
        {
            get
            {
                var cam = Camera.main;
                return TapPicker.PixelsPerUnit(CanvasScale, cam != null ? cam.pixelWidth : Screen.width, cam != null ? cam.pixelHeight : Screen.height);
            }
        }

        // Whether an input-taking graphic lies under the point. Walks the raycastable graphics itself instead of
        // asking EventSystem.IsPointerOverGameObject: that answers for the input module's own pointer of the last
        // UI update, which is the wrong one when mouse and touchscreen are both present.
        public static bool UiBlocks(Vector2 screenPos, out string blocker)
        {
            blocker = null;
            foreach (var raycaster in Resources.FindObjectsOfTypeAll<GraphicRaycaster>())
            {
                if (raycaster == null || !raycaster.isActiveAndEnabled || !raycaster.gameObject.scene.IsValid()) continue;
                var canvas = raycaster.GetComponent<Canvas>();
                if (canvas == null || !canvas.enabled) continue;
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
                for (int i = 0; i < graphics.Count; i++)
                {
                    var g = graphics[i];
                    if (g == null || !g.raycastTarget || !g.isActiveAndEnabled || g.canvasRenderer.cull) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, screenPos, cam)) continue;
                    if (!g.Raycast(screenPos, cam)) continue;
                    blocker = canvas.name + "/" + g.transform.parent.name + "/" + g.name;
                    return true;
                }
            }
            return false;
        }

        void CancelPresses()
        {
            _mousePress.tracking = false;
            _touchPress.tracking = false;
        }

        void BeginPress(ref Press press, int id, Vector2 pos)
        {
            press.tracking = true;
            press.id = id;
            press.pos = pos;
            press.time = Time.unscaledTime;
            press.flags = default;
            press.flags.overUi = UiBlocks(pos, out _);
        }

        void TrackPress(ref Press press)
        {
            if (touch == null) return;
            if (touch.IsPointerOnStick(press.id) && touch.StickDeflected) press.flags.steered = true;
        }

        void EndPress(ref Press press, Vector2 releasePos)
        {
            press.tracking = false;
            press.flags.seconds = Time.unscaledTime - press.time;
            press.flags.movedPixels = (releasePos - press.pos).magnitude;
            Release(press.flags, releasePos);
        }

        // Mouse and touchscreen are read side by side: a laptop with a touch display reports a Touchscreen even
        // while the player uses the mouse.
        void UpdateTap()
        {
            var m = Mouse.current;
            if (m != null)
            {
                Vector2 p = m.position.ReadValue();
                if (m.leftButton.wasPressedThisFrame) BeginPress(ref _mousePress, -1, p);
                if (_mousePress.tracking)
                {
                    TrackPress(ref _mousePress);
                    if (m.leftButton.wasReleasedThisFrame) EndPress(ref _mousePress, p);
                    else if (!m.leftButton.isPressed) _mousePress.tracking = false;
                }
            }

            var ts = Touchscreen.current;
            if (ts == null) return;
            var t = ts.primaryTouch;
            var phase = t.phase.ReadValue();
            int id = t.touchId.ReadValue();
            bool down = phase == TouchPhase.Began || phase == TouchPhase.Moved || phase == TouchPhase.Stationary;
            if (down && (!_touchPress.tracking || _touchPress.id != id))
            {
                BeginPress(ref _touchPress, id, t.startPosition.ReadValue());
                _lastTouchId = id;
            }
            if (_touchPress.tracking)
            {
                if (down)
                {
                    int active = 0;
                    foreach (var o in ts.touches)
                    {
                        var op = o.phase.ReadValue();
                        if (op == TouchPhase.Began || op == TouchPhase.Moved || op == TouchPhase.Stationary) active++;
                    }
                    if (active > 1) _touchPress.flags.multiTouch = true;
                    if (touch != null && touch.IsPinching) _touchPress.flags.pinching = true;
                    TrackPress(ref _touchPress);
                }
                else if (phase == TouchPhase.Ended && id == _touchPress.id) EndPress(ref _touchPress, t.position.ReadValue());
                else _touchPress.tracking = false;
            }
            else if (phase == TouchPhase.Ended && id != _lastTouchId)
            {
                // A quick tap can begin and end between two frames; the Began phase is then never seen.
                _lastTouchId = id;
                BeginPress(ref _touchPress, id, t.startPosition.ReadValue());
                EndPress(ref _touchPress, t.position.ReadValue());
            }
        }

        // The end of a press: gates it and, if it was a tap, picks. Public so it can be driven without input devices.
        public TapReject Release(TapPress press, Vector2 screenPos)
        {
            bool playing = !Application.isPlaying || (session != null && session.Current == GameSession.State.Playing && !_photoActive && !_journalOpen && !_album.IsOpen);
            var reject = TapPicker.Gate(playing, press, tapHoldSeconds, tapSlop * PixelsPerUnit);
            if (reject == TapReject.None)
            {
                // A simulated touchscreen (or an OS that mirrors touch to the mouse) reports the same tap twice.
                if (Time.unscaledTime - _lastTapTime < 0.12f && (screenPos - _lastTapPos).sqrMagnitude < 400f) return LastReject;
                _lastTapTime = Time.unscaledTime;
                _lastTapPos = screenPos;
                if (!Tap(screenPos)) reject = TapReject.NothingNear;
            }
            else if (debugTaps)
            {
                UiBlocks(screenPos, out string blocker);
                Debug.Log($"WatchTools tap at {screenPos}: rejected ({reject}) after {press.seconds:F2} s, moved {press.movedPixels:F0} px" + (blocker != null ? ", UI under it: " + blocker : ""));
            }
            LastReject = reject;
            return reject;
        }

        // Picks the creature whose projection is closest to the tap within pickRadius: every animal (young ones
        // too) of every herd, and every visible critter, on all islands near the camera.
        public bool Tap(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            float radius = pickRadius * PixelsPerUnit;
            float best = float.MaxValue, bestCritter = float.MaxValue;
            IslandHerdSystem bestHerds = null;
            IslandCrittersSystem bestCritters = null;
            Island bestIsland = null, bestCritterIsland = null;
            int bestHerd = -1, bestMember = -1, bestIndex = -1, searched = 0;
            Vector3 c = cam.transform.position;
            Vector2 camXZ = new Vector2(c.x, c.z);
            Vector2 focusXZ = Following ? new Vector2(_followFocus.x, _followFocus.z) : player != null ? player.PlanarPosition : camXZ;
            foreach (var island in Island.All)
            {
                if (island == null) continue;
                float reach = tapRange + island.BoundingRadius;
                Vector2 ip = island.PlanarPosition;
                if ((ip - camXZ).sqrMagnitude > reach * reach && (ip - focusXZ).sqrMagnitude > reach * reach) continue;
                var tr = island.transform;
                var herds = island.GetComponent<IslandHerdSystem>();
                if (herds != null && herds.isActiveAndEnabled && herds.Tier != LifeTier.Far)
                {
                    int hn = herds.HerdCount;
                    for (int h = 0; h < hn; h++)
                    {
                        int mn = herds.HerdSize(h);
                        float lift = herds.BodyLength(h) * 0.35f;
                        for (int m = 0; m < mn; m++)
                        {
                            searched++;
                            Vector3 local = herds.AnimalLocalPosition(h, m);
                            local.y += lift * herds.AnimalSizeFactor(h, m);
                            if (!TapPicker.Consider(screenPos, cam.WorldToScreenPoint(tr.TransformPoint(local)), radius, ref best)) continue;
                            bestHerds = herds; bestIsland = island; bestHerd = h; bestMember = m;
                        }
                    }
                }
                var critters = island.GetComponent<IslandCrittersSystem>();
                if (critters == null || !critters.isActiveAndEnabled || critters.Tier == LifeTier.Far) continue;
                int cn = critters.CritterCount;
                for (int i = 0; i < cn; i++)
                {
                    if (!CritterVisible(critters, i)) continue;
                    searched++;
                    if (!TapPicker.Consider(screenPos, cam.WorldToScreenPoint(CritterWorld(island, critters, i)), radius, ref bestCritter)) continue;
                    bestCritters = critters; bestCritterIsland = island; bestIndex = i;
                }
            }

            // Herd animals win a near tie: they are what a tap into a meadow is aimed at.
            bool critter = bestCritters != null && (bestHerds == null || bestCritter * 1.3f < best);
            bool hit = critter || bestHerds != null;
            Ripple(screenPos, hit);
            if (debugTaps)
                Debug.Log(hit
                    ? $"WatchTools tap at {screenPos}: picked {(critter ? bestCritters.KindOf(bestIndex) : bestHerds.HerdKind(bestHerd))} at {(critter ? bestCritter : best):F0} px (radius {radius:F0} px, {searched} candidates)"
                    : $"WatchTools tap at {screenPos}: nothing within {radius:F0} px ({searched} candidates)");
            if (!hit)
            {
                HidePopup();
                return false;
            }
            if (critter) ShowCritterPopup(bestCritterIsland, bestCritters, bestIndex);
            else ShowPopup(bestIsland, bestHerds, bestHerd, bestMember);
            return true;
        }

        static bool CritterVisible(IslandCrittersSystem critters, int i) =>
            !critters.DyingOf(i) && critters.FadeOf(i) >= 0.5f && critters.StateOf(i) != CritterState.Hidden;

        static Vector3 CritterWorld(Island island, IslandCrittersSystem critters, int i)
        {
            Vector2 p = critters.PositionOf(i);
            return island.transform.TransformPoint(p.x, Mathf.Max(0f, island.SampleHeight(p)), p.y);
        }

        void Ripple(Vector2 screenPos, bool hit)
        {
            if (_ripple == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPos, null, out var local);
            _ripple.anchoredPosition = local;
            _ripple.localScale = new Vector3(0.5f, 0.5f, 1f);
            _rippleImage.color = UiStyle.WithAlpha(hit ? UiStyle.Sand : UiStyle.Cream, 0.7f);
            _rippleT = 0f;
            _ripple.gameObject.SetActive(Application.isPlaying);
        }

        // ---------------------------------------------------------------- popup

        public void ShowPopup(Island island, IslandHerdSystem herds, int herd, int member)
        {
            _popupIsland = island;
            _popupHerds = herds;
            _popupCritters = null;
            _popupHerdIndex = herd;
            _popupMember = member;
            _popupKind = herds.HerdKind(herd);
            OpenPopup(true);
        }

        public void ShowCritterPopup(Island island, IslandCrittersSystem critters, int index)
        {
            _popupIsland = island;
            _popupHerds = null;
            _popupCritters = critters;
            _popupHerdIndex = -1;
            _popupMember = index;
            _popupKind = critters.KindOf(index);
            OpenPopup(false);
        }

        void OpenPopup(bool canFollow)
        {
            _popupTimer = popupSeconds + popupFadeSeconds;
            _shownState = _shownSize = _shownYoung = -1;
            SetActive(_popupFollow, canFollow);
            string origin = canFollow && _popupHerds != null && _popupHerds.IsForeign(_popupKind) ? OriginLine(_popupKind) : "";
            LayoutPopup(canFollow, origin);
            _popup.SetActive(true);
            _marker.gameObject.SetActive(true);
            _popupGroup.alpha = 1f;
            UpdatePopup();
        }

        public void HidePopup()
        {
            _popupHerds = null;
            _popupCritters = null;
            _popupIsland = null;
            _popupHerdIndex = _popupMember = -1;
            if (_popup != null && _popup.activeSelf) _popup.SetActive(false);
            if (_marker != null && _marker.gameObject.activeSelf) _marker.gameObject.SetActive(false);
        }

        bool PopupValid
        {
            get
            {
                if (_popupIsland == null || _popupMember < 0) return false;
                if (_popupCritters != null)
                    return _popupCritters.isActiveAndEnabled && _popupMember < _popupCritters.CritterCount
                        && _popupCritters.KindOf(_popupMember) == _popupKind && CritterVisible(_popupCritters, _popupMember);
                return _popupHerds != null && _popupHerds.isActiveAndEnabled
                    && _popupHerdIndex >= 0 && _popupHerdIndex < _popupHerds.HerdCount
                    && _popupHerds.HerdKind(_popupHerdIndex) == _popupKind
                    && _popupMember < _popupHerds.HerdSize(_popupHerdIndex);
            }
        }

        void UpdatePopup()
        {
            if (_popupHerds == null && _popupCritters == null) return;
            if (!PopupValid) { HidePopup(); return; }
            if (Application.isPlaying) _popupTimer -= Time.unscaledDeltaTime;
            if (_popupTimer <= 0f) { HidePopup(); return; }
            float alpha = Mathf.Clamp01(_popupTimer / Mathf.Max(0.01f, popupFadeSeconds));
            _popupGroup.alpha = alpha;

            Vector3 world;
            float body;
            if (_popupCritters != null)
            {
                int state = (int)_popupCritters.StateOf(_popupMember);
                if (state != _shownState)
                {
                    _shownState = state;
                    FillCritterPopup(_popupKind, state);
                }
                world = CritterWorld(_popupIsland, _popupCritters, _popupMember);
                body = 0.12f;
            }
            else
            {
                int state = (int)_popupHerds.AnimalStateOf(_popupHerdIndex, _popupMember) + 100 * (int)_popupHerds.ActivityOf(_popupHerdIndex, _popupMember);
                int size = _popupHerds.HerdSize(_popupHerdIndex);
                int young = _popupHerds.IsYoung(_popupHerdIndex, _popupMember) ? 1 : 0;
                if (state != _shownState || size != _shownSize || young != _shownYoung)
                {
                    _shownState = state; _shownSize = size; _shownYoung = young;
                    FillPopup(_popupKind, young == 1, size, state % 100);
                    string mood = _popupHerds.MoodOf(_popupHerdIndex, _popupMember);
                    if (!string.IsNullOrEmpty(mood)) _popupMood.text = "Stimmung: " + mood;
                }
                world = _popupIsland.transform.TransformPoint(_popupHerds.AnimalLocalPosition(_popupHerdIndex, _popupMember));
                body = _popupHerds.BodyLength(_popupHerdIndex) * _popupHerds.AnimalSizeFactor(_popupHerdIndex, _popupMember);
            }

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(world);
            float bodyPixels = (cam.WorldToScreenPoint(world + cam.transform.right * body) - screen).magnitude;
            float ring = PlaceMarker(screen, bodyPixels, alpha);
            PlacePopup(screen, ring * 0.5f + 14f);
        }

        public static string NameOf(LifeKind kind) => LifeNames.Of(kind);

        // "Fremde Art von tropischen Inseln": the extra popup line of a species that came with a merge.
        public static string OriginLine(LifeKind kind)
        {
            string from = CollectionCatalog.OriginOf(kind);
            return from.Length > 0 ? "Fremde Art " + from : "Fremde Art";
        }

        // The card grows by one line for a foreign species; the follow button slides down with it.
        void LayoutPopup(bool canFollow, string origin)
        {
            _popupForeign = !string.IsNullOrEmpty(origin);
            float extra = _popupForeign ? 40f : 0f;
            if (_popupOrigin != null)
            {
                _popupOrigin.text = origin ?? "";
                SetActive(_popupOrigin.gameObject, _popupForeign);
            }
            if (_popupFollowRect != null) _popupFollowRect.TopCenter(new Vector2(0f, -180f - extra), new Vector2(400f, 88f));
            _popupRect.sizeDelta = new Vector2(500f, (canFollow ? 292f : 184f) + extra);
        }

        void FillPopup(LifeKind kind, bool young, int size, int state)
        {
            _popupTitle.text = young ? NameOf(kind) + "  ·  Jungtier" : NameOf(kind);
            _popupHerd.text = size == 1 ? "Allein unterwegs" : "Herde: " + size + " Tiere";
            _popupMood.text = "Stimmung: " + (state >= 0 && state < Moods.Length ? Moods[state] : "");
        }

        void FillCritterPopup(LifeKind kind, int state)
        {
            _popupTitle.text = NameOf(kind);
            _popupHerd.text = kind == LifeKind.Crab || kind == LifeKind.Turtle ? "Lebt am Strand" : "Schwirrt über die Insel";
            _popupMood.text = state >= 0 && state < CritterMoods.Length ? CritterMoods[state] : "";
        }

        // A soft ring on the ground under the picked creature; returns its height in canvas units.
        float PlaceMarker(Vector3 screen, float bodyPixels, float alpha)
        {
            if (screen.z <= 0f) { _markerGroup.alpha = 0f; return 0f; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local);
            float diameter = Mathf.Clamp(bodyPixels * 2.6f / CanvasScale, 46f, 240f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
            _marker.anchoredPosition = local;
            _marker.localScale = new Vector3(diameter / 56f, diameter / 56f * 0.62f, 1f);
            _markerGroup.alpha = alpha * (0.7f + 0.3f * pulse);
            return diameter * 0.62f;
        }

        void PlacePopup(Vector3 screen, float lift)
        {
            if (screen.z <= 0f) { _popupGroup.alpha = 0f; return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local);
            var rect = _root.rect;
            var size = _popupRect.sizeDelta;
            // While following, the return button and the herd chip own the top centre: the card then hangs below the animal.
            bool below = Following && local.y + lift + size.y > rect.yMax - FollowUiBottom;
            local.y = below ? local.y - lift - size.y : local.y + lift;
            local.x = Mathf.Clamp(local.x, rect.xMin + size.x * 0.5f, rect.xMax - size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, rect.yMin, rect.yMax - size.y);
            _popupRect.anchoredPosition = local;
        }

        // ---------------------------------------------------------------- follow

        public void FollowPopupHerd()
        {
            if (_popupHerds == null || !PopupValid) return;
            FollowHerd(_popupIsland, _popupHerds, _popupHerdIndex);
            HidePopup();
        }

        // The camera leaves the island and orbits the herd; the island is not steered and does not sink meanwhile.
        public void FollowHerd(Island island, IslandHerdSystem herds, int herd)
        {
            if (island == null || herds == null || herd < 0 || herd >= herds.HerdCount) return;
            Resolve();
            if (chaseCamera == null) return;
            _followIsland = island;
            _followHerds = herds;
            _followHerd = herd;
            _followHerdCount = herds.HerdCount;
            _followCenter = herds.HerdCenter(herd);
            _followKind = herds.HerdKind(herd);
            _followRadius = TargetFollowRadius();
            _chipSize = _chipMood = -1;
            _chipTimer = 0f;
            UpdateFollow();
            if (!Following) return;
            if (!BeginDrive())
            {
                ReturnToIsland();
                return;
            }
            SetFollowHome();
            _rig.GoHome();
            if (session != null)
            {
                session.FollowInputHold = true;
                session.FollowSinkHold = true;
            }
            if (touch != null) touch.Release();
        }

        // Watch the nearest example of a journal entry (a herd is followed with its herd chip). Closes the journal and
        // the pause menu. False when there is none in reach right now; the news chip then says so.
        public bool Watch(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= CollectionCatalog.Count) return false;
            Resolve();
            if (player == null || chaseCamera == null) return false;
            var e = CollectionCatalog.At(catalogIndex);
            var subject = WatchSubjects.Find(e, player, player.PlanarPosition, flocks, fish, seaLife, ships);
            if (subject == null)
            {
                ShowNotice(e.name + " ist gerade nicht in der Nähe");
                return false;
            }
            if (_journalOpen) CloseJournal();
            if (_album.IsOpen) _album.Close();
            if (_photoActive) ExitPhotoMode();
            if (session != null && session.Current == GameSession.State.Paused) session.Resume();
            if (Following) ReturnToIsland();
            HidePopup();
            if (subject.herds != null)
            {
                FollowHerd(subject.ground, subject.herds, subject.herd);
                return Following;
            }
            if (subject.focus == null || !subject.focus(out Vector3 focus)) return false;
            _observe = subject;
            _followIsland = subject.ground;
            _followFocus = focus;
            _followRadius = subject.radius;
            if (_followChipText != null) _followChipText.text = subject.label;
            if (!BeginDrive())
            {
                ReturnToIsland();
                return false;
            }
            SetFollowHome();
            _rig.GoHome();
            if (session != null)
            {
                session.FollowInputHold = true;
                session.FollowSinkHold = true;
            }
            if (touch != null) touch.Release();
            return true;
        }

        // A one-line message in the news chip, outside the collection queue.
        void ShowNotice(string text)
        {
            _toasts.Dismiss();
            _noticeTimer = 2.6f;
            ShowNews(new CollectionToast { text = text, strong = false });
            SetActive(_newsChip, true);
        }

        public void ReturnToIsland()
        {
            if (!Following) return;
            _observe = null;
            _followHerds = null;
            _followIsland = null;
            _followHerd = -1;
            if (session != null)
            {
                session.FollowInputHold = false;
                session.FollowSinkHold = false;
            }
            if (!_photoActive) EndDrive();
            else if (_driving && Camera.main != null)
            {
                // The herd went away under a running photo mode: the island becomes the subject, the camera stays put.
                Rebase(Camera.main);
                _rig.HomeFromTargets();
            }
        }

        // R in follow mode: the chase framing behind the island's heading at followZoomLevel.
        void SetFollowHome()
        {
            float zoom = Mathf.Max(chaseCamera.zoomMin, followZoomLevel);
            var basis = _followIsland != null ? _followIsland : player;
            Vector3 back = basis != null ? -basis.Forward : Vector3.back;
            IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, back, _followRadius, chaseCamera.referenceRadius,
                chaseCamera.zoomExponent, chaseCamera.height, chaseCamera.distanceBehind, zoom, out Vector3 pos, out _, out _);
            float yaw = 0f, pitch = 40f, dist = 4f;
            PhotoRig.OrbitOf(-pos, ref yaw, ref pitch, ref dist);
            _rig.SetHome(yaw, pitch, dist);
        }

        // Herd indices shift when an earlier herd dies out; the followed herd is then found again as the herd of
        // the same kind whose centre is where the followed one was.
        bool ResolveFollowHerd()
        {
            if (_followHerds == null || _followIsland == null || !_followHerds.isActiveAndEnabled) return false;
            if (_followIsland.IsSunk || _followIsland.LandArea <= 0f) return false;
            int n = _followHerds.HerdCount;
            bool ok = _followHerd >= 0 && _followHerd < n && _followHerds.HerdKind(_followHerd) == _followKind && _followHerds.HerdSize(_followHerd) > 0
                && (n >= _followHerdCount || (_followHerds.HerdCenter(_followHerd) - _followCenter).sqrMagnitude < 4f);
            if (!ok)
            {
                int found = -1;
                float bestSqr = 9f;
                for (int h = 0; h < n; h++)
                {
                    if (_followHerds.HerdKind(h) != _followKind || _followHerds.HerdSize(h) == 0) continue;
                    float d = (_followHerds.HerdCenter(h) - _followCenter).sqrMagnitude;
                    if (d >= bestSqr) continue;
                    bestSqr = d;
                    found = h;
                }
                if (found < 0) return false;
                _followHerd = found;
            }
            _followHerdCount = n;
            _followCenter = _followHerds.HerdCenter(_followHerd);
            return true;
        }

        float TargetFollowRadius() => Mathf.Max(1.5f, _followHerds.HerdRadius(_followHerd) + 1f);

        Vector3 HerdFocus()
        {
            var c = _followHerds.HerdCenter(_followHerd);
            return _followIsland.transform.TransformPoint(c.x, Mathf.Max(0f, _followIsland.SampleHeight(c)), c.y);
        }

        void UpdateFollow()
        {
            if (!Following) return;
            if (_observe != null)
            {
                if (!_observe.focus(out Vector3 f))
                {
                    // Sea life is recycled beyond its range from the player and critters leave: say so instead
                    // of silently cutting back to the island.
                    string gone = _observe.label + " ist weitergezogen";
                    ReturnToIsland();
                    ShowNotice(gone);
                    return;
                }
                _followFocus = f;
                float odt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
                _followRadius = Mathf.Lerp(_followRadius, _observe.radius, 1f - Mathf.Exp(-1.5f * odt));
                return;
            }
            if (!ResolveFollowHerd()) { ReturnToIsland(); return; }
            _followFocus = HerdFocus();
            // The framing radius (zoom limits) is eased so a herd that spreads out or huddles does not pump the camera.
            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
            _followRadius = Mathf.Lerp(_followRadius, TargetFollowRadius(), 1f - Mathf.Exp(-1.5f * dt));

            _chipTimer -= dt;
            if (_chipTimer > 0f && _chipSize >= 0) return;
            _chipTimer = 0.4f;
            int size = _followHerds.HerdSize(_followHerd);
            int mood = HerdMood(_followHerds, _followHerd);
            if (size == _chipSize && mood == _chipMood) return;
            _chipSize = size;
            _chipMood = mood;
            FillFollowChip(_followKind, size, mood);
        }

        // Asked again in LateUpdate, after the islands have moved this frame.
        Vector3 FollowFocusOf()
        {
            if (_observe != null)
            {
                if (_observe.focus(out Vector3 f)) _followFocus = f;
                return _followFocus;
            }
            if (_followHerds != null && _followIsland != null && _followHerd >= 0 && _followHerd < _followHerds.HerdCount) _followFocus = HerdFocus();
            return _followFocus;
        }

        int HerdMood(IslandHerdSystem herds, int herd)
        {
            Array.Clear(_moodCount, 0, _moodCount.Length);
            int n = herds.HerdSize(herd), best = 0;
            for (int m = 0; m < n; m++)
            {
                int st = (int)herds.AnimalStateOf(herd, m);
                if (st >= 0 && st < _moodCount.Length) _moodCount[st]++;
            }
            for (int i = 1; i < _moodCount.Length; i++) if (_moodCount[i] > _moodCount[best]) best = i;
            return best;
        }

        public static string PluralOf(LifeKind kind) => LifeNames.Plural(kind);

        public static string FollowChipLine(LifeKind kind, int size, int mood)
        {
            string m = mood >= 0 && mood < Moods.Length ? Moods[mood] : "";
            return size == 1 ? NameOf(kind) + "  ·  allein  ·  " + m : PluralOf(kind) + "  ·  " + size + " Tiere  ·  " + m;
        }

        void FillFollowChip(LifeKind kind, int size, int mood)
        {
            if (_followChipText != null) _followChipText.text = FollowChipLine(kind, size, mood);
        }

        // ---------------------------------------------------------------- discovery

        // Counter growth since the last scan of that component; the first scan only records the current value.
        static int Delta<T>(Dictionary<T, int> seen, T key, int value)
        {
            if (seen.TryGetValue(key, out int prev))
            {
                seen[key] = value;
                return value - prev;
            }
            seen[key] = value;
            return 0;
        }

        void UpdateDiscovery()
        {
            _discoverTimer -= Time.unscaledDeltaTime;
            if (_discoverTimer > 0f) return;
            _discoverTimer = discoverInterval;
            if (saveManager == null || player == null) return;
            Vector3 focus = Following ? _followFocus : player.transform.position;
            Scan(saveManager.Journal, new Vector2(focus.x, focus.z), session != null ? session.Stats.timeSurvived : 0f);
        }

        void NoteSeen(DiscoveryJournal journal, int index, float time)
        {
            if (journal.MarkSeen(index, time)) _toasts.Seen(index);
        }

        void NoteSeen(DiscoveryJournal journal, ulong lifeMask, float time)
        {
            for (ulong fresh = journal.MarkSeen(lifeMask, time); fresh != 0; fresh &= fresh - 1)
                _toasts.Seen(CollectionCatalog.IndexOf((LifeKind)LowestBit(fresh)));
        }

        static int LowestBit(ulong v)
        {
            int n = 0;
            while ((v & 1UL) == 0) { v >>= 1; n++; }
            return n;
        }

        // Once per discoverInterval. What lives on the player's island becomes "collected"; what is within
        // discoverRange of the focus on other islands (herds and critters of near-tier systems, their plants), in
        // the air and at sea becomes "seen"; and the birth/fire counters of the islands in range add up.
        // It only reads the simulation; a species mask that holds nothing new costs one compare.
        public void Scan(DiscoveryJournal journal, Vector2 focus, float time)
        {
            // The first scan of a run (new or loaded) is where it starts from, not news.
            bool first = !journal.Baselined;
            if (player != null) ScanPlayerIsland(journal, time);
            bool all = journal.AllSeen;
            float range = discoverRange, range2 = range * range;
            var islands = Island.All;
            for (int n = 0; n < islands.Count; n++)
            {
                var island = islands[n];
                if (island == null) continue;
                float reach = range + island.BoundingRadius;
                if ((island.PlanarPosition - focus).sqrMagnitude > reach * reach) continue;
                var tr = island.transform;
                if (island.TryGetComponent(out IslandHerdSystem herds))
                {
                    journal.AddYoungBorn(Delta(_birthsSeen, herds, herds.Births));
                    if (!all && island != player && herds.Tier == LifeTier.Near && (herds.SpeciesPresent & journal.UnseenLifeMask) != 0)
                    {
                        int hn = herds.HerdCount;
                        for (int h = 0; h < hn; h++)
                        {
                            if (herds.HerdSize(h) == 0) continue;
                            var c = herds.HerdCenter(h);
                            Vector3 w = tr.TransformPoint(c.x, 0f, c.y);
                            if ((new Vector2(w.x, w.z) - focus).sqrMagnitude > range2) continue;
                            NoteSeen(journal, CollectionCatalog.IndexOf(herds.HerdKind(h)), time);
                        }
                    }
                }
                if (island.TryGetComponent(out IslandLifeSystem life))
                {
                    journal.AddFiresSeen(Delta(_firesSeen, life, life.IgnitionCount));
                    if (island != player && (journal.UnseenLifeMask & CollectionCatalog.PlantMask) != 0) NoteSeen(journal, life.PlantsPresent, time);
                }
                if (all || island == player || (journal.UnseenLifeMask & CollectionCatalog.CritterMask) == 0) continue;
                if (!island.TryGetComponent(out IslandCrittersSystem critters) || critters.Tier != LifeTier.Near) continue;
                int cn = critters.CritterCount;
                for (int i = 0; i < cn; i++)
                {
                    if (!CritterVisible(critters, i)) continue;
                    var p = critters.PositionOf(i);
                    Vector3 w = tr.TransformPoint(p.x, 0f, p.y);
                    if ((new Vector2(w.x, w.z) - focus).sqrMagnitude > range2) continue;
                    NoteSeen(journal, CollectionCatalog.IndexOf(critters.KindOf(i)), time);
                }
            }
            if (!all) ScanSeaAndSky(journal, focus, range2, time);
            if (first) _toasts.Clear();
        }

        void ScanSeaAndSky(DiscoveryJournal journal, Vector2 focus, float range2, float time)
        {
            if (flocks != null)
            {
                int fn = flocks.FlockCount;
                for (int i = 0; i < fn; i++)
                {
                    if ((flocks.PositionOf(i) - focus).sqrMagnitude > range2) continue;
                    NoteSeen(journal, CollectionCatalog.IndexOf(flocks.IsSeabird(i) ? LifeKind.Seabird : LifeKind.Bird), time);
                }
            }
            if (fish != null)
            {
                int sn = fish.SchoolSlots;
                for (int i = 0; i < sn; i++)
                {
                    if (!fish.SchoolActive(i) || (fish.SchoolPosition(i) - focus).sqrMagnitude > range2) continue;
                    NoteSeen(journal, fish.IsBaitBall(i) ? CollectionCatalog.IndexOf(SeaKind.BaitBall) : CollectionCatalog.FishIndex, time);
                }
            }
            if (seaLife != null && seaLife.isActiveAndEnabled)
            {
                int gn = seaLife.GroupSlots;
                for (int i = 0; i < gn; i++)
                {
                    if (!seaLife.GroupActive(i) || (seaLife.GroupPosition(i) - focus).sqrMagnitude > range2) continue;
                    var kind = seaLife.GroupKind(i);
                    if (kind == SeaLifeSystem.Kind.Seaweed) continue;
                    bool whale = kind == SeaLifeSystem.Kind.Whale || kind == SeaLifeSystem.Kind.WhalePod;
                    // State 0 of a whale is the dive: nothing to see.
                    if (whale && seaLife.GroupState(i) == 0) continue;
                    if (kind == SeaLifeSystem.Kind.Gulls) NoteSeen(journal, CollectionCatalog.IndexOf(LifeKind.Seabird), time);
                    else if (kind == SeaLifeSystem.Kind.Whale) NoteSeen(journal, CollectionCatalog.IndexOf(SeaKind.WhaleBull), time);
                    else NoteSeen(journal, CollectionCatalog.IndexOf(SeaLifeSystem.SeaKindOf(kind)), time);
                    if (seaLife.GroupHasCalf(i)) NoteSeen(journal, CollectionCatalog.IndexOf(SeaKind.WhaleCalf), time);
                }
                // The bursts always start a few units off the player's island.
                if (seaLife.FlyingFishActive && player != null && (player.PlanarPosition - focus).sqrMagnitude < range2)
                    NoteSeen(journal, CollectionCatalog.IndexOf(SeaKind.FlyingFish), time);
            }
            if (ships != null && ships.isActiveAndEnabled)
            {
                int sn = ships.ShipSlots;
                for (int i = 0; i < sn; i++)
                {
                    if (!ships.ShipActive(i) || (ships.ShipPosition(i) - focus).sqrMagnitude > range2) continue;
                    NoteSeen(journal, CollectionCatalog.IndexOf(ShipSystem.SeaKindOf(ships.ShipKindOf(i))), time);
                }
            }
        }

        static readonly LifeKind[] CritterKinds = { LifeKind.Crab, LifeKind.Turtle, LifeKind.Butterfly, LifeKind.Firefly };

        // The collection itself: what lives on the player's island right now. The presence mask is compared as a
        // whole; herd and critter counts are refreshed every scan, plant counts one species per scan (a count
        // walks every plant) and all of them when the journal opens.
        void ScanPlayerIsland(DiscoveryJournal journal, float time)
        {
            ulong mask = 0;
            bool hasHerds = player.TryGetComponent(out IslandHerdSystem herds);
            if (hasHerds) mask |= herds.SpeciesPresent;
            bool hasLife = player.TryGetComponent(out IslandLifeSystem life);
            if (hasLife) mask |= life.PlantsPresent;
            if (player.TryGetComponent(out IslandCrittersSystem critters))
                for (int i = 0; i < CritterKinds.Length; i++)
                {
                    int count = critters.CountOf(CritterKinds[i]);
                    if (count > 0) mask |= 1UL << (int)CritterKinds[i];
                    journal.SetCount(CritterKinds[i], count);
                }

            bool first = !journal.Baselined;
            ulong fresh = journal.ReportPresence(mask, time);
            if (!first)
                for (ulong rest = fresh; rest != 0; rest &= rest - 1)
                    _toasts.Collected(CollectionCatalog.IndexOf((LifeKind)LowestBit(rest)));

            if (hasHerds)
                for (ulong rest = mask & CollectionCatalog.AnimalMask; rest != 0; rest &= rest - 1)
                {
                    var kind = (LifeKind)LowestBit(rest);
                    journal.SetCount(kind, herds.AnimalsOf(kind));
                }
            ulong plants = mask & CollectionCatalog.PlantMask;
            if (hasLife && plants != 0)
            {
                do _plantCursor = (_plantCursor + 1) & 63;
                while ((plants & (1UL << _plantCursor)) == 0);
                journal.SetCount((LifeKind)_plantCursor, life.CountOfKind((LifeKind)_plantCursor));
            }
            if (first) RefreshPlantCounts(journal);
            if (player.TryGetComponent(out IslandSettlementSystem settlement)) journal.ReportStage((int)settlement.Stage);

            if (lifeBook && Application.isPlaying && (first || fresh != 0) && LifeBook.Add(journal.CollectedLifeMask)) _lifeBook.Save();
        }

        void RefreshPlantCounts(DiscoveryJournal journal)
        {
            if (player == null || !player.TryGetComponent(out IslandLifeSystem life)) return;
            for (ulong rest = life.PlantsPresent & CollectionCatalog.PlantMask; rest != 0; rest &= rest - 1)
            {
                var kind = (LifeKind)LowestBit(rest);
                journal.SetCount(kind, life.CountOfKind(kind));
            }
        }

        // ---------------------------------------------------------------- journal

        // Opening from the running game pauses it; the pause menu stays underneath and the game resumes on close.
        public void OpenJournal()
        {
            if (_journalOpen) return;
            Resolve();
            _journalPausedSession = Application.isPlaying && session != null && session.Current == GameSession.State.Playing && session.Model.Pause();
            _journalOpen = true;
            _journalVersion = -1;
            _journalTimer = 0f;
            HidePopup();
            if (Application.isPlaying && Journal != null) RefreshPlantCounts(Journal);
            _journal.SetActive(true);
            UpdateJournal();
        }

        // 0-3 the biomes, 4 "Meer & Himmel", 5 "Insulaner".
        public void ShowJournalTab(int tab, int page = 0) => _journalPanel.SetTab(tab, page);

        public void CloseJournal()
        {
            if (!_journalOpen) return;
            _journalOpen = false;
            _journal.SetActive(false);
            if (_journalPausedSession && session != null && session.Current == GameSession.State.Paused) session.Resume();
            _journalPausedSession = false;
        }

        void UpdateJournal()
        {
            var j = Journal;
            if (j == null) return;
            _journalTimer -= Time.unscaledDeltaTime;
            if (j.Version == _journalVersion && _journalTimer > 0f) return;
            _journalTimer = journalRefresh;
            _journalVersion = j.Version;
            _journalPanel.Fill(j, lifeBook ? LifeBook : null, IslandInfo());
        }

        readonly System.Text.StringBuilder _buildingLine = new System.Text.StringBuilder(160);
        static readonly BuildingKind[] BuildingKinds = (BuildingKind[])Enum.GetValues(typeof(BuildingKind));

        // The read-only island and settlement numbers of the journal's last tab.
        JournalIslandInfo IslandInfo()
        {
            var info = new JournalIslandInfo { absorbed = session != null ? session.Stats.islandsAbsorbed : 0 };
            if (player == null) return info;
            ulong present = 0;
            if (player.TryGetComponent(out IslandHerdSystem herds))
            {
                info.animals = herds.AnimalCount;
                info.herds = herds.HerdCount;
                present |= herds.SpeciesPresent;
            }
            if (player.TryGetComponent(out IslandLifeSystem life))
            {
                info.plants = life.LivePlantCount;
                info.trees = life.CountOf(LifeKind.Tree) + life.CountOf(LifeKind.Palm);
                info.biome = "Biom: " + life.BiomeName;
                present |= life.PlantsPresent;
            }
            info.foreignKinds = DiscoveryJournal.BitCount(present & CollectionCatalog.CollectibleMask & ~Biomes.Of(player.Biome).mask);
            if (player.TryGetComponent(out IslandSettlementSystem settlement))
            {
                info.stage = settlement.StageName;
                info.settlers = settlement.SettlerCount;
                info.villages = settlement.VillageCount;
                info.buildings = settlement.BuildingCount;
                info.buildingLine = BuildingLine(settlement);
            }
            return info;
        }

        string BuildingLine(IslandSettlementSystem settlement)
        {
            _buildingLine.Length = 0;
            int shown = 0;
            for (int k = 0; k < BuildingKinds.Length; k++)
            {
                int n = settlement.CountOf(BuildingKinds[k]);
                if (n == 0) continue;
                if (shown > 0) _buildingLine.Append(shown % 3 == 0 ? "\n" : "  ·  ");
                _buildingLine.Append(JournalPanel.BuildingName(BuildingKinds[k])).Append(' ').Append(n);
                shown++;
            }
            return _buildingLine.ToString();
        }

        // ---------------------------------------------------------------- photo mode

        // From the HUD camera button or the pause menu: the game runs on (a paused one resumes) with the island's
        // input held, the HUD hidden, sinking held and the camera on the orbit rig. Entered while a herd is followed,
        // the herd stays the subject and the orbit carries over; Back then returns to following.
        public void EnterPhotoMode()
        {
            if (_photoActive) return;
            Resolve();
            if (chaseCamera == null || Camera.main == null) return;
            if (Application.isPlaying)
            {
                if (session == null) return;
                if (session.Current == GameSession.State.Paused) session.Resume();
                if (session.Current != GameSession.State.Playing) return;
            }
            if (_journalOpen) CloseJournal();
            _album.Close();
            HidePopup();
            CancelPresses();
            bool wasDriving = _driving;
            _photoActive = true;
            if (wasDriving) ConfigureRig();
            else if (BeginDrive()) _rig.HomeFromTargets();
            else
            {
                _photoActive = false;
                return;
            }
            if (session != null)
            {
                session.PhotoInputHold = true;
                session.PhotoSinkHold = true;
            }
            HudHidden = true;
            ResetPointers();
            SetToast("");
            _photo.SetActive(true);
            if (touch != null) touch.Release();
        }

        public void ExitPhotoMode()
        {
            if (!_photoActive) return;
            _photoActive = false;
            HudHidden = false;
            AbortCapture();
            _album.Close();
            if (_photo != null) _photo.SetActive(false);
            if (session != null)
            {
                session.PhotoSinkHold = false;
                session.PhotoInputHold = false;
            }
            ResetPointers();
            if (Following) ConfigureRig();
            else EndDrive();
        }

        public void CenterPhoto() => _rig.Center();

        public string PhotoDirectory => PhotoLibrary.DefaultDirectory;

        public void SavePhoto()
        {
            if (!_photoActive || _capturing || !Application.isPlaying) return;
            _captureRoutine = StartCoroutine(CaptureRoutine());
        }

        // Every canvas is switched off, one full frame is rendered without UI and read back at its end.
        IEnumerator CaptureRoutine()
        {
            _capturing = true;
            _hiddenCanvases.Clear();
            foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (c == null || !c.enabled || !c.isRootCanvas || !c.gameObject.scene.IsValid()) continue;
                c.enabled = false;
                _hiddenCanvases.Add(c);
            }
            yield return null;
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            bool ok = false;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                PhotoLibrary.Save(shot, PhotoDirectory, DateTime.Now);
                ok = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("WatchTools: photo failed: " + e.Message);
            }
            finally
            {
                PhotoLibrary.Dispose(shot);
                RestoreCanvases();
                _capturing = false;
                _captureRoutine = null;
            }
            if (ok) _flashT = 1f;
            SetToast(ok ? "Foto gespeichert" : "Speichern fehlgeschlagen");
            _toastTimer = ToastSeconds;
        }

        void AbortCapture()
        {
            if (_captureRoutine != null) StopCoroutine(_captureRoutine);
            _captureRoutine = null;
            _capturing = false;
            RestoreCanvases();
        }

        void RestoreCanvases()
        {
            foreach (var c in _hiddenCanvases) if (c != null) c.enabled = true;
            _hiddenCanvases.Clear();
        }

        // ---------------------------------------------------------------- album

        // Lies over whatever opened it (photo mode, pause menu, title); Back / Escape steps confirm -> picture -> grid -> closed.
        public void OpenAlbum()
        {
            if (_album.IsOpen) return;
            if (Application.isPlaying && session != null && session.Current == GameSession.State.GameOver) return;
            HidePopup();
            CancelPresses();
            ResetPointers();
            if (Application.isPlaying) _album.Open(PhotoDirectory);
            else _album.OpenSample(false);
        }

        public void CloseAlbum() => _album.Close();

        public void AlbumBack() => _album.Back();

        // ---------------------------------------------------------------- orbit camera

        Island SubjectIsland => Following ? _followIsland : player;

        // What the camera orbits: the followed herd's centre on the terrain, else the top of the player island's
        // centre. Asked every frame, so the pivot travels with a drifting island.
        Vector3 SubjectPosition()
        {
            if (Following) return FollowFocusOf();
            if (player == null) return _lastSubject;
            Vector3 p = player.transform.position;
            p.y += Mathf.Max(0f, player.SampleHeight(Vector2.zero));
            return p;
        }

        void ConfigureRig()
        {
            var island = SubjectIsland;
            float islandRadius = island != null ? island.BoundingRadius : 3f;
            _rig.orbitDegPerPixel = orbitDegPerPixel;
            float pan = _photoActive ? islandRadius * panRadiusFactor : 0f;
            if (Following) _rig.SetLimits(Mathf.Max(_followRadius, islandRadius * 0.5f), photoMinDistance, pan);
            else _rig.SetLimits(islandRadius, photoMinDistance, pan);
        }

        bool BeginDrive()
        {
            var cam = Camera.main;
            if (cam == null || chaseCamera == null) return false;
            if (!_driving)
            {
                _driving = true;
                _zoomBeforeDrive = chaseCamera.ZoomTarget;
                chaseCamera.Suspended = true;
            }
            Rebase(cam);
            return true;
        }

        // Takes the camera over where it stands, around the current subject; only the view direction has to turn
        // towards the subject, which the rotation blend covers.
        void Rebase(Camera cam)
        {
            ConfigureRig();
            _rig.FromPose(cam.transform.position, SubjectPosition());
            _rotBlendFrom = cam.transform.rotation;
            _rotBlend = 0f;
        }

        void EndDrive()
        {
            if (!_driving) return;
            _driving = false;
            ResetPointers();
            if (chaseCamera == null) return;
            chaseCamera.SetZoom(_zoomBeforeDrive);
            if (Application.isPlaying) chaseCamera.ResumeEased(returnEaseSeconds);
            else
            {
                chaseCamera.Suspended = false;
                chaseCamera.SnapToTarget();
            }
        }

        void ResetPointers()
        {
            _dragging = _mouseOrbit = _panning = _pinching = false;
            _input = default;
        }

        void AddZoom(float factor)
        {
            if (factor > 0f) _input.zoomFactor = (_input.zoomFactor > 0f ? _input.zoomFactor : 1f) * factor;
        }

        // Mouse, touchscreen and keyboard are read side by side into one OrbitInput; panning is photo mode only.
        void GatherInput()
        {
            var ts = Touchscreen.current;
            if (ts != null) TouchOrbit(ts);
            if (!_pinching) MouseOrbit();
            KeyOrbit();
        }

        void MouseOrbit()
        {
            var m = Mouse.current;
            if (m == null) return;
            Vector2 p = m.position.ReadValue();
            if (m.leftButton.wasPressedThisFrame && !UiBlocks(p, out _)) { _mouseOrbit = true; _lastMouse = p; }
            if (_photoActive && (m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame) && !UiBlocks(p, out _)) { _panning = true; _lastMouse = p; }
            if (_mouseOrbit && !m.leftButton.isPressed) _mouseOrbit = false;
            if (_panning && !m.rightButton.isPressed && !m.middleButton.isPressed) _panning = false;
            Vector2 d = p - _lastMouse;
            if (_panning) { _input.panX += d.x; _input.panY += d.y; }
            else if (_mouseOrbit) { _input.orbitX += d.x; _input.orbitY += d.y; }
            if (_mouseOrbit || _panning) _lastMouse = p;
            float sc = m.scroll.ReadValue().y;
            if (Mathf.Abs(sc) > 0.01f && !UiBlocks(p, out _)) AddZoom(Mathf.Exp(-sc * (chaseCamera != null ? chaseCamera.scrollZoomSpeed : 0.0012f)));
        }

        void TouchOrbit(Touchscreen ts)
        {
            int n = 0;
            Vector2 p0 = default, p1 = default;
            int id0 = 0;
            bool began0 = false;
            foreach (var t in ts.touches)
            {
                var phase = t.phase.ReadValue();
                if (phase != TouchPhase.Began && phase != TouchPhase.Moved && phase != TouchPhase.Stationary) continue;
                if (n == 0) { p0 = t.position.ReadValue(); id0 = t.touchId.ReadValue(); began0 = phase == TouchPhase.Began; }
                else if (n == 1) p1 = t.position.ReadValue();
                n++;
            }
            if (n >= 2)
            {
                _dragging = false;
                float dist = Vector2.Distance(p0, p1);
                Vector2 center = (p0 + p1) * 0.5f;
                if (_pinching)
                {
                    if (_pinchDist > 1f && dist > 1f) AddZoom(_pinchDist / dist);
                    if (_photoActive)
                    {
                        Vector2 d = center - _pinchCenter;
                        _input.panX += d.x;
                        _input.panY += d.y;
                    }
                }
                _pinching = true;
                _pinchDist = dist;
                _pinchCenter = center;
                return;
            }
            _pinching = false;
            if (n == 0) { _dragging = false; return; }
            if (began0 && !UiBlocks(p0, out _)) { _dragging = true; _dragId = id0; _lastPointer = p0; return; }
            if (!_dragging || id0 != _dragId) return;
            Vector2 delta = p0 - _lastPointer;
            _input.orbitX += delta.x;
            _input.orbitY += delta.y;
            _lastPointer = p0;
        }

        // A/D (left/right) around the subject, W/S (up/down) tilt, Q/E zoom, R back to the starting orbit.
        void KeyOrbit()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float yaw = 0f, pitch = 0f, zoom = 0f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) yaw += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) yaw -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pitch += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pitch -= 1f;
            if (kb.qKey.isPressed || kb.numpadPlusKey.isPressed) zoom += 1f;
            if (kb.eKey.isPressed || kb.numpadMinusKey.isPressed) zoom -= 1f;
            _input.keyYaw = yaw;
            _input.keyPitch = pitch;
            _input.keyZoom = zoom;
            if (kb.rKey.wasPressedThisFrame) _input.reset = true;
        }

        // Highest terrain under a world XZ point (sea level when no island is there).
        static float GroundHeight(Vector2 xz)
        {
            float y = 0f;
            foreach (var island in Island.All)
            {
                if (island == null) continue;
                float reach = island.BoundingRadius + 1f;
                if ((xz - island.PlanarPosition).sqrMagnitude > reach * reach) continue;
                float h = island.SampleHeight(island.ToLocal(xz));
                if (h > -0.2f) y = Mathf.Max(y, island.transform.position.y + Mathf.Max(0f, h));
            }
            return y;
        }

        // One camera frame: input into the rig, the pivot re-attached to the subject where it is now, the pose kept
        // above every island under it. Public (with an injectable input) so it can be driven from eval.
        public void StepCamera(in OrbitInput input, float dt)
        {
            var cam = Camera.main;
            if (!_driving || cam == null) return;
            ConfigureRig();
            _rig.Step(input, dt);
            Vector3 subject = SubjectPosition();
            _lastSubject = subject;
            _rig.EaseHeight(GroundHeight(_rig.PivotPlanar(subject)), dt);
            _rig.Pose(subject, out Vector3 pos, out Quaternion rot);
            Vector3 pivot = _rig.Pivot(subject);

            float close = PhotoRig.CloseBlend(_rig.distance);
            float clearance = Mathf.Lerp(photoGroundClearance, chaseCamera != null ? chaseCamera.closeGroundClearance : 0.22f, close);
            var subjectIsland = SubjectIsland;
            float minY = photoMinHeight;
            foreach (var island in Island.All)
            {
                if (island == null) continue;
                float reach = island.BoundingRadius + 1f;
                Vector2 d = new Vector2(pos.x, pos.z) - island.PlanarPosition;
                if (d.sqrMagnitude > reach * reach) continue;
                minY = Mathf.Max(minY, IslandChaseCamera.RequiredHeight(island, pos, pivot, clearance, island == subjectIsland ? close : 0f));
            }
            if (pos.y < minY)
            {
                pos.y = minY;
                Vector3 look = pivot - pos;
                if (look.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
            if (_rotBlend < 1f)
            {
                _rotBlend = Mathf.Min(1f, _rotBlend + dt / 0.5f);
                rot = Quaternion.Slerp(_rotBlendFrom, rot, Mathf.SmoothStep(0f, 1f, _rotBlend));
            }
            cam.transform.SetPositionAndRotation(pos, rot);
            if (chaseCamera != null) chaseCamera.SuspendedClose = close;
        }

        public static string OrbitHint(bool touchInput, bool photo)
        {
            if (touchInput) return photo ? "Ziehen: drehen  ·  Zwei Finger: Zoom und verschieben" : "Ziehen: drehen  ·  Zwei Finger: Zoom";
            return photo ? "A/D drehen  ·  W/S neigen  ·  Q/E Zoom  ·  R zurück\nRechte Maustaste: verschieben" : "A/D drehen  ·  W/S neigen  ·  Q/E Zoom  ·  R zurück";
        }

        // Following: under the herd chip at the top. Photo mode: above the button rows at the bottom.
        void UpdateHint(bool visible, bool photo)
        {
            if (_hintChip == null) return;
            SetActive(_hintChip, visible);
            if (!visible) return;
            bool touchInput = Application.isPlaying && InputMode.TouchPreferred;
            int key = (touchInput ? 1 : 0) | (photo ? 2 : 0);
            if (key == _hintShown) return;
            _hintShown = key;
            _hintText.text = OrbitHint(touchInput, photo);
            var size = new Vector2(photo && !touchInput ? 900f : 820f, photo && !touchInput ? 96f : 60f);
            if (photo) _hintRect.BottomCenter(new Vector2(0f, 308f), size);
            else _hintRect.TopCenter(new Vector2(0f, -556f), size);
        }

        // ---------------------------------------------------------------- editor preview

        void EditorPreviewUpdate()
        {
            bool pop = editorPreview == EditorPreview.Popup, jr = editorPreview == EditorPreview.Journal, ph = editorPreview == EditorPreview.Photo;
            bool album = editorPreview == EditorPreview.Album || editorPreview == EditorPreview.AlbumPhoto;
            // A popup or a followed herd staged from eval stays up (and live) so it can be looked at in Edit Mode.
            // Photo mode entered from eval counts too; the camera only moves when eval calls StepCamera.
            bool staged = editorPreview == EditorPreview.None && (Following || _photoActive || _popupHerds != null || _popupCritters != null);
            if (staged)
            {
                UpdateFollow();
                UpdatePopup();
                SetActive(_followButton, Following && !_photoActive);
                SetActive(_followChip, Following && !_photoActive);
                SetActive(_bookButton, !_photoActive);
                SetActive(_cameraButton, !_photoActive);
                SetActive(_photo, _photoActive);
                UpdateHint(_driving, _photoActive);
                HudHidden = _photoActive;
                return;
            }
            HudHidden = ph;
            UpdateHint(ph || pop, ph);
            SetActive(_popup, pop);
            SetActive(_marker.gameObject, pop);
            SetActive(_followButton, pop);
            SetActive(_followChip, pop);
            SetActive(_journal, jr);
            SetActive(_photo, ph);
            SetActive(_bookButton, pop);
            SetActive(_cameraButton, pop);
            SetActive(_ripple.gameObject, pop);
            if (_albumPreview != editorPreview)
            {
                _albumPreview = editorPreview;
                _album.Close();
                if (album) _album.OpenSample(editorPreview == EditorPreview.AlbumPhoto);
            }
            if (album) _album.Tick();
            SetActive(_newsChip, pop);
            if (pop)
            {
                _popupGroup.alpha = 1f;
                SetActive(_popupFollow, true);
                LayoutPopup(true, OriginLine(LifeKind.Flamingo));
                FillPopup(LifeKind.Flamingo, true, 7, (int)AnimalState.Play);
                FillFollowChip(LifeKind.Sheep, 7, (int)AnimalState.Graze);
                ShowNews(new CollectionToast { text = CollectionToasts.CollectedText(new[] { CollectionCatalog.IndexOf(LifeKind.Flamingo) }, 1, 1), strong = true });
                _newsRect.anchoredPosition = new Vector2(0f, -FollowUiBottom - 24f);
                _popupRect.anchoredPosition = new Vector2(60f, -160f);
                _marker.anchoredPosition = new Vector2(60f, -200f);
                _marker.localScale = new Vector3(1.5f, 0.93f, 1f);
                _markerGroup.alpha = 0.9f;
                _ripple.anchoredPosition = new Vector2(60f, -200f);
                _ripple.localScale = new Vector3(1.7f, 1.7f, 1f);
                _rippleImage.color = UiStyle.WithAlpha(UiStyle.Sand, 0.35f);
            }
            if (jr)
            {
                if (_sampleJournal == null) _sampleJournal = SampleJournal(_sampleBook);
                _journalPanel.Fill(_sampleJournal, _sampleBook, SampleIsland);
            }
            if (ph) SetToast("Foto gespeichert");
        }

        static readonly JournalIslandInfo SampleIsland = new JournalIslandInfo
        {
            biome = "Biom: Gemäßigt", stage = "Dorf", animals = 48, herds = 9, plants = 812, trees = 96, absorbed = 4, foreignKinds = 6,
            buildings = 14, settlers = 16, villages = 1, buildingLine = "Lagerfeuer 1  ·  Hütte 5  ·  Brunnen 1\nGarten 2  ·  Steg 1  ·  Haus 3\nWindmühle 1",
        };

        readonly LifeBook _sampleBook = new();

        static ulong Bits(params LifeKind[] kinds)
        {
            ulong m = 0;
            foreach (var k in kinds) m |= 1UL << (int)k;
            return m;
        }

        // A run some way in, for the Edit Mode preview: natives collected, a goat herd lost again, a merge that
        // brought tropical species, and a few sightings at sea.
        static DiscoveryJournal SampleJournal(LifeBook book)
        {
            var j = new DiscoveryJournal();
            ulong start = Bits(LifeKind.Grass, LifeKind.Flower, LifeKind.Bush, LifeKind.Tree, LifeKind.Palm, LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Crab);
            j.ReportPresence(start, 0f);
            j.MarkSeen(LifeKind.Bird, 95f);
            j.MarkSeen(CollectionCatalog.FishIndex, 140f);
            j.MarkSeen(LifeKind.Zebra, 260f);
            j.MarkSeen(LifeKind.Acacia, 260f);
            j.MarkSeen(LifeKind.DryGrass, 260f);
            j.MarkSeen(LifeKind.Spruce, 330f);
            j.MarkSeen(LifeKind.Penguin, 334f);
            j.MarkSeen(LifeKind.Capybara, 402f);
            ulong merged = (start & ~Bits(LifeKind.Goat)) | Bits(LifeKind.Flamingo, LifeKind.Tortoise, LifeKind.Fern, LifeKind.Hibiscus, LifeKind.Banana, LifeKind.Butterfly, LifeKind.Reed);
            j.ReportPresence(merged, 431f);
            j.MarkSeen(LifeKind.Seabird, 512f);
            j.MarkSeen(SeaKind.Dolphin, 548f);
            j.MarkSeen(SeaKind.SailBoat, 612f);
            j.MarkSeen(SeaKind.FlyingFish, 655f);
            j.MarkSeen(SeaKind.WhaleBull, 701f);
            j.MarkSeen(LifeKind.Firefly, 820f);
            j.SetCount(LifeKind.Goat, 6);
            j.SetCount(LifeKind.Goat, 0);
            j.SetCount(LifeKind.Hare, 14);
            j.SetCount(LifeKind.Sheep, 18);
            j.SetCount(LifeKind.Sheep, 11);
            j.SetCount(LifeKind.Flamingo, 7);
            j.SetCount(LifeKind.Tortoise, 3);
            j.SetCount(LifeKind.Grass, 412);
            j.SetCount(LifeKind.Flower, 96);
            j.SetCount(LifeKind.Bush, 74);
            j.SetCount(LifeKind.Tree, 88);
            j.SetCount(LifeKind.Palm, 8);
            j.SetCount(LifeKind.Reed, 21);
            j.SetCount(LifeKind.Fern, 38);
            j.SetCount(LifeKind.Hibiscus, 12);
            j.SetCount(LifeKind.Banana, 9);
            j.SetCount(LifeKind.Crab, 5);
            j.SetCount(LifeKind.Butterfly, 9);
            j.AddYoungBorn(3);
            j.AddFiresSeen(1);
            j.ReportStage((int)SettlementStage.Village);
            book.Clear();
            book.Add(j.CollectedLifeMask | Bits(LifeKind.Ox, LifeKind.Zebra, LifeKind.Giraffe, LifeKind.Spruce));
            return j;
        }

        // ---------------------------------------------------------------- layout

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, true, out _root);

            _bookButton = BuildBookButton(_root);
            _cameraButton = BuildCameraButton(_root);
            _marker = BuildMarker(_root);
            _ripple = BuildRipple(_root);
            _popup = BuildPopup(_root);
            _followButton = BuildFollowButton(_root);
            _followChip = BuildFollowChip(_root);
            _journal = _journalPanel.Build(_root, CloseJournal, index => Watch(index));
            _photo = BuildPhoto(_root);
            _newsChip = BuildNews(_root);
            _hintChip = BuildHint(_root);
            _album.Build(_root, null);
            _albumPreview = EditorPreview.None;

            _popup.SetActive(false);
            _marker.gameObject.SetActive(false);
            _ripple.gameObject.SetActive(false);
            _followButton.SetActive(false);
            _followChip.SetActive(false);
            _journal.SetActive(false);
            _newsChip.SetActive(false);
            _photo.SetActive(false);
            _hintChip.SetActive(false);
            _bookButton.SetActive(false);
            _cameraButton.SetActive(false);
        }

        // The round buttons stack under the pause button of SessionScreens (120 wide, top right): book, then camera.
        GameObject BuildBookButton(RectTransform root)
        {
            var b = UiStyle.IconButton(root, "JournalButton", 120f, UiIcon.Book, OpenJournal);
            ((RectTransform)b.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiStyle.Margin, -UiStyle.Margin - 148f), new Vector2(120f, 120f));
            return b.gameObject;
        }

        GameObject BuildCameraButton(RectTransform root)
        {
            var b = UiStyle.IconButton(root, "PhotoButton", 120f, UiIcon.Camera, EnterPhotoMode);
            ((RectTransform)b.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-UiStyle.Margin, -UiStyle.Margin - 296f), new Vector2(120f, 120f));
            return b.gameObject;
        }

        RectTransform BuildMarker(RectTransform root)
        {
            var marker = UiStyle.Rect(root, "PickMarker").Center(Vector2.zero, new Vector2(56f, 56f));
            _markerGroup = marker.gameObject.AddComponent<CanvasGroup>();
            _markerGroup.blocksRaycasts = false;
            _markerGroup.interactable = false;
            UiStyle.Shape(marker, "Glow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Sand, 0.28f)).rectTransform.Stretch(-14f, -14f, -14f, -14f);
            UiStyle.Shape(marker, "Ring", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Sand, 0.9f)).rectTransform.Stretch();
            return marker;
        }

        RectTransform BuildRipple(RectTransform root)
        {
            _rippleImage = UiStyle.Shape(root, "TapRipple", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Cream, 0.7f));
            return _rippleImage.rectTransform.Center(Vector2.zero, new Vector2(56f, 56f));
        }

        // Only the button takes input, so an animal half under the card can still be tapped.
        GameObject BuildPopup(RectTransform root)
        {
            var size = new Vector2(500f, 292f);
            _popupRect = UiStyle.Panel(root, "AnimalPopup", size, true, false).Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, size);
            _popupGroup = _popupRect.gameObject.AddComponent<CanvasGroup>();
            _popupTitle = PopupLine("", 40, UiStyle.Sand, -22f, 52f, true);
            _popupHerd = PopupLine("", 30, UiStyle.Cream, -80f, 40f, false);
            _popupMood = PopupLine("", 30, UiStyle.CreamSoft, -120f, 40f, false);
            _popupOrigin = PopupLine("", 28, UiStyle.Sky, -160f, 38f, false);
            _popupOrigin.gameObject.SetActive(false);
            var follow = UiStyle.PrimaryButton(_popupRect, "Follow", "Herde folgen", new Vector2(400f, 88f), FollowPopupHerd);
            _popupFollowRect = ((RectTransform)follow.transform).TopCenter(new Vector2(0f, -180f), new Vector2(400f, 88f));
            _popupFollow = follow.gameObject;
            return _popupRect.gameObject;
        }

        Text PopupLine(string text, int size, Color color, float y, float height, bool bold)
        {
            var t = UiStyle.Label(_popupRect, text, size, color, TextAnchor.UpperCenter, bold);
            t.rectTransform.TopCenter(new Vector2(0f, y), new Vector2(440f, height));
            return UiStyle.FitWidth(t);
        }

        GameObject BuildFollowButton(RectTransform root)
        {
            var b = UiStyle.PrimaryButton(root, "ReturnToIsland", "Zurück zur Insel", new Vector2(680f, 120f), ReturnToIsland);
            ((RectTransform)b.transform).TopCenter(new Vector2(0f, -340f), new Vector2(680f, 120f));
            return b.gameObject;
        }

        GameObject BuildFollowChip(RectTransform root)
        {
            var chip = UiStyle.Chip(root, "FollowChip", "", new Vector2(680f, 68f), out _followChipText);
            chip.TopCenter(new Vector2(0f, -476f), new Vector2(680f, 68f));
            _followChipText.fontSize = 32;
            _followChipText.color = UiStyle.Cream;
            return chip.gameObject;
        }

        // Collection news: one chip under the HUD panel (under the follow controls while a herd is followed).
        GameObject BuildNews(RectTransform root)
        {
            var size = new Vector2(700f, 76f);
            _newsRect = UiStyle.Chip(root, "CollectionNews", "", size, out _newsText);
            _newsRect.TopCenter(new Vector2(0f, NewsTop), size);
            _newsText.rectTransform.Stretch(84f, 0f, UiStyle.Gap + 8f, 2f);
            _newsIcon = UiStyle.Icon(_newsRect, "Icon", UiIcon.Book, 42f, UiStyle.Sand);
            _newsIcon.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(50f, 0f), new Vector2(42f, 42f));
            // Tapping a toast shows what it is about: one entry is watched right away, several open the journal
            // on their tab. The chevron says it can be tapped.
            var body = _newsRect.Find("Body");
            var bodyImage = body != null ? body.GetComponent<Image>() : null;
            if (bodyImage != null) bodyImage.raycastTarget = true;
            var button = _newsRect.gameObject.AddComponent<Button>();
            button.targetGraphic = bodyImage;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(OnNewsTapped);
            _newsRect.gameObject.AddComponent<UiPressFeedback>().pressedScale = 0.96f;
            _newsGo = UiStyle.Icon(_newsRect, "Go", UiIcon.Back, 30f, UiStyle.Sand);
            _newsGo.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(30f, 30f));
            _newsGo.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            _newsText.rectTransform.Stretch(84f, 0f, 70f, 2f);
            UiStyle.FadeIn(_newsRect.gameObject, null);
            return _newsRect.gameObject;
        }

        GameObject BuildPhoto(RectTransform root)
        {
            var rt = UiStyle.Rect(root, "PhotoMode").Stretch();
            Letterbox(rt, "LetterboxTop", 1f);
            Letterbox(rt, "LetterboxBottom", 0f);

            var toast = UiStyle.Chip(rt, "PhotoToast", "", new Vector2(520f, 76f), out _photoToast);
            toast.BottomCenter(new Vector2(0f, 424f), new Vector2(520f, 76f));
            _photoToast.fontSize = 34;
            _photoToast.color = UiStyle.Sand;
            _toastChip = toast.gameObject;
            _toastChip.SetActive(false);

            var save = UiStyle.PrimaryButton(rt, "SavePhoto", "Foto speichern", new Vector2(420f, 120f), SavePhoto);
            PhotoButton(save, -294f, 420f);
            UiStyle.ButtonIcon(save, UiIcon.Camera);
            var album = UiStyle.SecondaryButton(rt, "OpenAlbum", "Album", new Vector2(270f, 120f), OpenAlbum, false, true);
            PhotoButton(album, 75f, 270f);
            UiStyle.ButtonIcon(album, UiIcon.Album);
            var leave = UiStyle.SecondaryButton(rt, "LeavePhoto", "Zurück", new Vector2(270f, 120f), ExitPhotoMode, false, true);
            PhotoButton(leave, 369f, 270f);
            UiStyle.ButtonIcon(leave, UiIcon.Back);
            var center = UiStyle.SecondaryButton(rt, "CenterPhoto", "Zentrieren", new Vector2(360f, 96f), CenterPhoto, false, true);
            ((RectTransform)center.transform).BottomCenter(new Vector2(0f, 196f), new Vector2(360f, 96f));
            UiStyle.LabelOf(center).fontSize = 36;

            _flash = UiStyle.Shape(rt, "ShutterFlash", null, UiStyle.WithAlpha(UiStyle.Cream, 0f));
            _flash.rectTransform.Stretch(-200f, -200f, -200f, -200f);
            _flash.gameObject.SetActive(false);
            return rt.gameObject;
        }

        GameObject BuildHint(RectTransform root)
        {
            _hintRect = UiStyle.Chip(root, "OrbitHint", "", new Vector2(820f, 60f), out _hintText);
            _hintRect.TopCenter(new Vector2(0f, -556f), new Vector2(820f, 60f));
            _hintText.fontSize = 28;
            _hintText.color = UiStyle.CreamSoft;
            _hintShown = -1;
            return _hintRect.gameObject;
        }

        static void PhotoButton(Button b, float x, float width)
        {
            ((RectTransform)b.transform).BottomCenter(new Vector2(x, 60f), new Vector2(width, 120f));
            UiStyle.LabelOf(b).fontSize = 40;
        }

        static void Letterbox(RectTransform parent, string name, float anchorY)
        {
            var img = UiStyle.Shape(parent, name, null, UiStyle.WithAlpha(UiStyle.Dim, 0.62f));
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, anchorY);
            rt.anchorMax = new Vector2(1f, anchorY);
            rt.pivot = new Vector2(0.5f, anchorY);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 110f);
        }
    }
}
