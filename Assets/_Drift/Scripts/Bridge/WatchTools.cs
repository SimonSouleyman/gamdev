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
        public enum EditorPreview { None, Popup, Journal, Photo, Album, AlbumPhoto, JournalTasks }

        const string CanvasName = "WatchToolsCanvas";
        const float ToastSeconds = 3f;
        // Lower edge of "Zurück zur Insel" + herd chip, measured from the top of the safe area.
        const float FollowUiBottom = 700f;

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
        // Renamed from popupSeconds (4 s) when the owner asked for every creature notice to stay 3 s longer, so the
        // old value serialized in the scene does not override the new default.
        [Tooltip("So lange (Sekunden) bleibt die Karte eines angetippten Tiers stehen, bevor sie ausblendet.")]
        public float popupShowSeconds = 7f;
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

        [Header("Beobachten: Kamera")]
        [Tooltip("Neigung der Kamera beim Beobachten in Grad (90 = senkrecht von oben). Steiler heißt: weniger Bäume zwischen Kamera und Tieren.")]
        [Range(20f, 80f)] public float watchPitch = 55f;
        [Tooltip("So viel der halben kurzen Bildseite nimmt eine Herde zu Beginn des Beobachtens ein.")]
        [Range(0.2f, 1f)] public float watchFill = 0.55f;
        [Tooltip("So groß soll ein Tier beim Beobachten mindestens erscheinen (Pixel bei 1080 Pixel kurzer Bildseite). Für kleine Tiere wie Hasen kommt die Kamera dafür näher.")]
        [Range(0f, 120f)] public float watchMinBodyPixels = 44f;
        [Tooltip("Das beobachtete Tier steht in der Mitte des freien Bildbereichs unter „Zurück zur Insel“ und der Herdenzeile statt in der Bildmitte.")]
        public bool watchCenterBelowHud = true;
        [Tooltip("Zu Beginn des Beobachtens die Blickrichtung wählen, in der am wenigsten Bäume und Hügel zwischen Kamera und Tieren stehen (bevorzugt nahe der bisherigen Blickrichtung).")]
        public bool watchClearView = true;
        [Tooltip("Wie schnell die Kamera der Mitte der Herde folgt (pro Sekunde). Kleiner = ruhiger, größer = enger an den Tieren.")]
        [Range(0.5f, 10f)] public float watchFocusFollow = 3f;

        [Header("Beobachten: Bauwerke")]
        [Tooltip("Neigung der Kamera, wenn ein Bauwerk (Leuchtturm, Hafen, Festplatz) beobachtet wird - flacher als bei Tieren, damit man den Turm von der Seite sieht.")]
        [Range(10f, 70f)] public float stillPitch = 28f;
        [Tooltip("So viel der halben kurzen Bildseite nimmt das Bauwerk zu Beginn ein (kleiner = mehr Umgebung).")]
        [Range(0.1f, 1f)] public float stillFill = 0.55f;
        [Tooltip("Grad pro Sekunde, mit denen die Kamera langsam um das Bauwerk kreist, solange niemand sie bewegt (0 = steht still).")]
        [Range(0f, 30f)] public float stillOrbitDegPerSecond = 5f;
        [Tooltip("So viele Sekunden nach der letzten eigenen Kamerabewegung beginnt das langsame Kreisen wieder.")]
        [Range(0f, 10f)] public float stillOrbitDelay = 1.5f;

        [Header("Meldungen")]
        [Tooltip("Wie lange (Sekunden) eine Meldung „Neu auf deiner Insel …“ oder „Fotoaufgabe erfüllt“ stehen bleibt.")]
        [Range(2f, 15f)] public float newsStrongSeconds = 7f;
        [Tooltip("Wie lange (Sekunden) die leisere Meldung „Zum ersten Mal gesehen …“ stehen bleibt.")]
        [Range(2f, 15f)] public float newsSubtleSeconds = 5.6f;
        [Tooltip("Wie lange (Sekunden) ein Hinweis wie „… ist gerade nicht in der Nähe“ stehen bleibt.")]
        [Range(2f, 15f)] public float noticeSeconds = 5.6f;
        [Tooltip("Wie lange (Sekunden) ein antippbarer Fotoaufgaben-Hinweis stehen bleibt.")]
        [Range(2f, 15f)] public float photoHintSeconds = 7f;

        [Tooltip("Das Tagebuch der Arten bleibt über alle Reisen erhalten (eigene Datei drift_journal.json); aus = nur die laufende Reise.")]
        public bool keepJournal = true;

        [Header("Fotoaufgaben")]
        [Tooltip("Fotoaufgaben im Tagebuch: jede Art mit eigener Bewegung einmal dabei fotografieren. Der Fortschritt bleibt über alle Reisen erhalten.")]
        public bool photoTasks = true;
        [Tooltip("So groß muss ein Tier mindestens im Bild sein (Anteil an der kurzen Bildseite), damit das Foto die Aufgabe erfüllt.")]
        [Range(0.005f, 0.1f)] public float photoTaskMinSize = 0.015f;
        [Tooltip("Abstand zum Bildrand (Anteil der Bildbreite/-höhe), innerhalb dessen ein Tier nicht als \"im Bild\" zählt.")]
        [Range(0f, 0.2f)] public float photoTaskMargin = 0.04f;
        [Tooltip("In diesem Umkreis um den Kamerafokus zeigt ein kleines Kamera-Symbol über dem Tier, dass gerade eine offene Fotoaufgabe zu sehen ist.")]
        [Range(5f, 80f)] public float photoTaskHintRange = 30f;
        [Tooltip("Höchstens so oft (Sekunden) erscheint zusätzlich ein Hinweis wie \"Die Flamingos tanzen im Schlamm!\".")]
        [Range(30f, 900f)] public float photoTaskHintInterval = 180f;
        [Tooltip("Größe des gezeichneten Kamera-Symbols über dem Tier.")]
        [Range(64f, 160f)] public float cueSize = 96f;
        [Tooltip("Fingerfläche um das Kamera-Symbol: so weit daneben zählt ein Tippen noch als Treffer (halbe Kantenlänge).")]
        [Range(45f, 120f)] public float cueTapRadius = 64f;

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
        // The one thing being watched, whichever entry point started it; null = not watching. A herd brings its
        // system along (chip, popup, herd framing), anything else a focus function.
        WatchSubject _watch;
        IslandHerdSystem _followHerds;
        Island _followIsland;
        int _followHerd = -1, _followHerdCount;
        LifeKind _followKind;
        Vector2 _followCenter;
        Vector3 _followFocus;
        float _followRadius = 1.5f, _chipTimer;
        int _chipSize = -1, _chipMood = -1;
        // The camera watches the middle of the animals themselves (island frame, eased), not the herd's leading
        // centre point, which runs ahead of a moving herd; _herdSpread is the largest animal distance from it.
        Vector2 _focusLocal;
        bool _focusSnap = true;
        float _herdSpread, _lift;
        const int YawCandidates = 12;
        const float TreeHeight = 0.8f;
        Vector3[] _occluders = new Vector3[64];
        readonly float[] _yawCost = new float[YawCandidates];
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
            // The press went down on the photo-task cue; the release no longer has to land on it.
            public bool onCue;
        }

        Press _mousePress, _touchPress;
        int _lastTouchId = int.MinValue;
        Vector2 _lastTapPos;
        float _lastTapTime = -10f;

        float _discoverTimer, _lookupTimer;
        readonly Dictionary<IslandHerdSystem, int> _birthsSeen = new();
        readonly Dictionary<IslandLifeSystem, int> _firesSeen = new();

        JournalBook _journalBook;
        int _journalBookSaved = -1;
        float _journalBookTimer;
        PhotoTaskBook _photoTasks, _sampleTasks;
        int _tasksVersion = -1;
        readonly Dictionary<int, Texture2D> _taskThumbs = new();
        readonly List<PhotoSubject> _subjects = new();
        readonly List<int> _judgedTasks = new();
        readonly List<Vector2> _judgedCentres = new();
        PhotoSubject _cue;
        bool _cueActive, _noticePhoto;
        float _cueTimer, _hintToastTimer;
        GameObject _cueButton;
        RectTransform _cueRect, _cueHit;
        // Screen position of the drawn icon, refreshed every frame: a tap is measured against it, not against a
        // rect the finger has to hit twice.
        Vector2 _cueScreen;
        float _cueHitSize = -1f;

        public bool PhotoActive => _photoActive;
        public bool JournalOpen => _journalOpen;
        public bool Following => _watch != null;
        public bool Observing => _watch != null && _followHerds == null;
        public WatchSubject Watched => _watch;
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
        // Photo tasks done across all runs. Loaded from persistentDataPath on first use in Play Mode; in Edit Mode an
        // empty book that is never written. Tests put their own (temporary folder) book in.
        public PhotoTaskBook PhotoTasks
        {
            get
            {
                if (_photoTasks == null)
                {
                    _photoTasks = new PhotoTaskBook(Application.isPlaying ? PhotoTaskBook.DefaultDirectory : null);
                    _photoTasks.Load();
                }
                return _photoTasks;
            }
            set
            {
                _photoTasks = value;
                ClearTaskThumbs();
                _tasksVersion = -1;
            }
        }
        // The species journal across runs; attached to the save manager's journal in Play Mode (see Resolve).
        public JournalBook JournalBook
        {
            get
            {
                if (_journalBook == null)
                {
                    _journalBook = new JournalBook(Application.isPlaying ? JournalBook.DefaultDirectory : null);
                    _journalBook.Load();
                    _journalBookSaved = _journalBook.Version;
                }
                return _journalBook;
            }
            set
            {
                _journalBook = value;
                _journalBookSaved = value != null ? value.Version : -1;
            }
        }

        // Writes what the run added to the cross-run journal; cheap when nothing changed.
        public void FlushJournalBook()
        {
            var j = Journal;
            if (_journalBook == null || j == null || j.Book != _journalBook) return;
            _journalBook.Absorb(j);
            if (_journalBook.Version == _journalBookSaved) return;
            if (_journalBook.Save()) _journalBookSaved = _journalBook.Version;
        }

        void UpdateJournalBook(float dt)
        {
            // An adventure run must not add a line to the cozy books, so it never even attaches to them.
            if (!keepJournal || saveManager == null || !WatchRules.Allowed(WatchFeature.DiscoveryRecord)) return;
            var j = saveManager.Journal;
            if (j.Book == null) j.AttachBook(JournalBook);
            _journalBookTimer -= dt;
            if (_journalBookTimer > 0f) return;
            _journalBookTimer = 5f;
            FlushJournalBook();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Application.isPlaying) FlushJournalBook();
        }

        void OnApplicationQuit() => FlushJournalBook();

        public bool PhotoCueVisible => _cueButton != null && _cueButton.activeSelf;
        public int PhotoCueTask => _cueActive ? _cue.task : -1;
        public Vector2 PhotoCueScreen => _cueScreen;
        public RectTransform PhotoCueHitRect => _cueHit;
        public RectTransform PhotoCueIconRect => _cueRect;
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
            // The sparkle on tappable things; wired in the scene for tuning, added here when it is not.
            if (Application.isPlaying && !TryGetComponent(out TapSparkles _)) gameObject.AddComponent<TapSparkles>();
        }

        void OnDisable()
        {
            SetNearFade(false);
            if (_photoActive) ExitPhotoMode();
            if (Following) ReturnToIsland();
            _album.Close();
            _albumPreview = EditorPreview.None;
            HudHidden = false;
            ClearTaskThumbs();
            if (Application.isPlaying) FlushJournalBook();
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
            UpdateJournalBook(Time.unscaledDeltaTime);
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
                    if (!_photoActive && WatchRules.Allowed(WatchFeature.Watch)) UpdateTap();
                    if (_driving && !_capturing) GatherInput();
                }
                UpdateFollow();
                UpdatePopup();
                UpdateStillMarker();
                UpdateDiscovery();
            }

            if (_journalOpen) UpdateJournal();
            if (_album.IsOpen) _album.Tick();
            UpdateEffects(Time.unscaledDeltaTime);

            bool playing = s == GameSession.State.Playing;
            UpdateNews(playing && !_photoActive && !_journalOpen && !_album.IsOpen, Time.unscaledDeltaTime);
            bool hudButtons = playing && !_photoActive && !_journalOpen && !_album.IsOpen;
            SetActive(_bookButton, hudButtons && WatchRules.Allowed(WatchFeature.Journal));
            SetActive(_cameraButton, hudButtons && WatchRules.Allowed(WatchFeature.PhotoMode));
            SetActive(_followButton, playing && Following && !_photoActive);
            SetActive(_followChip, playing && Following && !_photoActive);
            SetActive(_photo, _photoActive && !_album.IsOpen);
            UpdateHint(playing && _driving && !_journalOpen && !_album.IsOpen, _photoActive);
            UpdatePhotoCue(playing && !_journalOpen && !_album.IsOpen && !_capturing, Time.unscaledDeltaTime);
            HudHidden = _photoActive;
        }

        // After the islands and herds have moved this frame, so the camera never trails its subject.
        void LateUpdate()
        {
            UpdateNearFade();
            if (!_driving || !Application.isPlaying) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            // A building does not move by itself: the camera circles it slowly while the player leaves it alone.
            if (Following && _watch != null && _watch.still && !_photoActive)
            {
                _stillIdle = _input.Any ? 0f : _stillIdle + dt;
                float rate = WatchFraming.StillOrbitRate(_stillIdle, stillOrbitDelay, 1.5f, stillOrbitDegPerSecond);
                if (rate > 0f) _input.keyYaw += rate / Mathf.Max(1f, _rig.keyYawSpeed);
            }
            StepCamera(_input, dt);
            _input = default;
            // The island is not steered while a herd is followed; a finger in the stick half orbits instead.
            if (Following && !_photoActive && touch != null && touch.StickActive) touch.Release();
        }

        // Watching and photo mode dissolve the plants between the orbit camera and its subject (Vegetation.shader's
        // DRIFT_NEAR_FADE): from low angles the trees next to a herd otherwise fill most of the picture.
        static readonly int NearFadeId = Shader.PropertyToID("_DriftNearFade");
        bool _nearFade;

        void UpdateNearFade()
        {
            bool on = Application.isPlaying && _driving && (Following || _photoActive);
            if (on)
            {
                float d = _rig.distance;
                Shader.SetGlobalVector(NearFadeId, new Vector4(Mathf.Max(1.2f, d - 1.2f), Mathf.Max(0.5f, d - 3f), 0f, 0f));
            }
            SetNearFade(on);
        }

        void SetNearFade(bool on)
        {
            if (on == _nearFade) return;
            _nearFade = on;
            if (on) Shader.EnableKeyword("DRIFT_NEAR_FADE");
            else Shader.DisableKeyword("DRIFT_NEAR_FADE");
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

        // The news chip was tapped (public so tests and eval take the same route as a finger).
        public void OnNewsTapped()
        {
            var t = _toasts.Current;
            if (_noticeTimer > 0f)
            {
                if (!_noticePhoto) return;
                _noticePhoto = false;
                _noticeTimer = 0f;
                SetActive(_newsChip, false);
                PhotographCue();
                return;
            }
            if (!_toasts.Showing || t.count <= 0) return;
            _toasts.Dismiss();
            SetActive(_newsChip, false);
            if (t.photo)
            {
                OpenJournal();
                _journalPanel.ShowTask(PhotoTaskCatalog.IndexOfEntry(t.first));
            }
            else if (t.count == 1) Watch(t.first);
            else
            {
                OpenJournal();
                ShowJournalTab((int)CollectionCatalog.At(t.first).section);
            }
        }

        void UpdateNews(bool visible, float dt)
        {
            _toasts.strongSeconds = newsStrongSeconds;
            _toasts.subtleSeconds = newsSubtleSeconds;
            // A mode without the collection has no news chip at all - a line queued in the previous run goes too.
            if (!WatchRules.Allowed(WatchFeature.DiscoveryToast))
            {
                _toasts.Clear();
                _noticeTimer = 0f;
                SetActive(_newsChip, false);
                return;
            }
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
            _newsIcon.color = toast.strong || toast.photo ? UiStyle.Sand : UiStyle.Muted;
            _newsIcon.sprite = UiSprites.Icon(toast.photo ? UiIcon.Camera : UiIcon.Book);
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

        // The game always draws this canvas as an overlay, which takes a null camera; an Edit Mode capture may put
        // it on a camera for one shot, and then every screen <-> canvas conversion needs that camera.
        Camera UiCamera => _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;

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
            press.onCue = CueContains(pos);
        }

        void TrackPress(ref Press press)
        {
            if (touch == null || !touch.IsPointerOnStick(press.id)) return;
            // A finger that went down on the cue is aiming at the cue, not at the thumbstick underneath it: the
            // stick lets go, so the island neither turns away nor swings the icon out from under the finger.
            if (press.onCue)
            {
                touch.Release();
                return;
            }
            if (touch.StickDeflected) press.flags.steered = true;
        }

        void EndPress(ref Press press, Vector2 releasePos)
        {
            press.tracking = false;
            press.flags.seconds = Time.unscaledTime - press.time;
            press.flags.movedPixels = (releasePos - press.pos).magnitude;
            Release(press.flags, releasePos, press.onCue || CueContains(releasePos));
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
        public TapReject Release(TapPress press, Vector2 screenPos) => Release(press, screenPos, false);

        // onCue: the press went down on (or came up on) the photo-task cue. The cue is UI, so the gate would throw
        // the tap away as overUi; it is also a target that walks off under the finger, so it is answered here
        // instead of by Unity's button.
        public TapReject Release(TapPress press, Vector2 screenPos, bool onCue)
        {
            bool playing = !Application.isPlaying || (session != null && session.Current == GameSession.State.Playing && !_photoActive && !_journalOpen && !_album.IsOpen);
            var gated = press;
            if (onCue) gated.overUi = false;
            var reject = TapPicker.Gate(playing, gated, tapHoldSeconds, tapSlop * PixelsPerUnit);
            if (reject == TapReject.None)
            {
                // A simulated touchscreen (or an OS that mirrors touch to the mouse) reports the same tap twice.
                if (Time.unscaledTime - _lastTapTime < 0.12f && (screenPos - _lastTapPos).sqrMagnitude < 400f) return LastReject;
                _lastTapTime = Time.unscaledTime;
                _lastTapPos = screenPos;
                if (onCue)
                {
                    if (debugTaps) Debug.Log($"WatchTools tap at {screenPos}: photo cue for task {PhotoCueTask}");
                    PhotographCue();
                }
                else if (!Tap(screenPos)) reject = TapReject.NothingNear;
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
            if (cam == null || !WatchRules.Allowed(WatchFeature.Watch)) return false;
            float radius = pickRadius * PixelsPerUnit;
            float best = float.MaxValue, bestCritter = float.MaxValue;
            IslandHerdSystem bestHerds = null;
            IslandCrittersSystem bestCritters = null;
            Island bestIsland = null, bestCritterIsland = null;
            int bestHerd = -1, bestMember = -1, bestIndex = -1, searched = 0;
            Vector3 c = cam.transform.position;
            Vector2 camXZ = new Vector2(c.x, c.z);
            Vector2 focusXZ = TapFocusXZ;
            foreach (var island in Island.All)
            {
                if (island == null || !TapTargets.InReach(island, camXZ, focusXZ, tapRange)) continue;
                var herds = island.GetComponent<IslandHerdSystem>();
                if (herds != null && herds.isActiveAndEnabled && herds.Tier != LifeTier.Far)
                {
                    int hn = herds.HerdCount;
                    for (int h = 0; h < hn; h++)
                    {
                        int mn = herds.HerdSize(h);
                        for (int m = 0; m < mn; m++)
                        {
                            searched++;
                            if (!TapPicker.Consider(screenPos, cam.WorldToScreenPoint(TapTargets.AnimalWorld(island, herds, h, m)), radius, ref best)) continue;
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
            // Seals (sea visitors at any coast) are watched straight away: there is no creature card for them.
            int seal = !hit && seaLife != null && seaLife.isActiveAndEnabled ? WatchSubjects.SealAtScreen(seaLife, cam, screenPos, radius) : -1;
            if (seal >= 0)
            {
                Ripple(screenPos, true);
                return BeginWatch(WatchSubjects.OfSeal(seaLife, seal));
            }
            // Flocks, dolphins, turtles, surfacing whales and the lighthouse or harbour are watched straight away too;
            // an animal under the finger still wins unless the other one is clearly closer.
            int extra = PickExtra(cam, screenPos, radius, camXZ, focusXZ, out float extraPx);
            if (extra >= 0 && (!hit || extraPx * 1.3f < (critter ? bestCritter : best)))
            {
                var subject = TapTargets.SubjectOf(_tapExtras[extra]);
                if (debugTaps) Debug.Log($"WatchTools tap at {screenPos}: picked {_tapExtras[extra].kind} at {extraPx:F0} px");
                if (subject != null)
                {
                    Ripple(screenPos, true);
                    return BeginWatch(subject);
                }
            }
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

        static bool CritterVisible(IslandCrittersSystem critters, int i) => TapTargets.CritterVisible(critters, i);

        static Vector3 CritterWorld(Island island, IslandCrittersSystem critters, int i) => TapTargets.CritterWorld(island, critters, i);

        readonly List<TapTarget> _tapExtras = new(32);

        // The flock, sea animal or landmark nearest to the tap on screen within radiusPx; -1 if none.
        int PickExtra(Camera cam, Vector2 screenPos, float radiusPx, Vector2 camXZ, Vector2 focusXZ, out float distancePx)
        {
            _tapExtras.Clear();
            TapTargets.Gather(_tapExtras, camXZ, focusXZ, tapRange, flocks, seaLife, TapGather.Extras);
            float bestPx = float.MaxValue;
            int pick = -1;
            for (int i = 0; i < _tapExtras.Count; i++)
                if (TapPicker.Consider(screenPos, cam.WorldToScreenPoint(_tapExtras[i].world), radiusPx, ref bestPx)) pick = i;
            distancePx = pick >= 0 ? bestPx : -1f;
            return pick;
        }

        // Where the tap search is centred besides the camera: the watched subject, else the player's island.
        public Vector2 TapFocusXZ
        {
            get
            {
                if (Following) return new Vector2(_followFocus.x, _followFocus.z);
                if (player != null) return player.PlanarPosition;
                var cam = Camera.main;
                return cam != null ? new Vector2(cam.transform.position.x, cam.transform.position.z) : Vector2.zero;
            }
        }

        void Ripple(Vector2 screenPos, bool hit)
        {
            if (_ripple == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPos, UiCamera, out var local);
            _ripple.anchoredPosition = local;
            _ripple.localScale = new Vector3(0.5f, 0.5f, 1f);
            _rippleImage.color = UiStyle.WithAlpha(hit ? UiStyle.Sand : UiStyle.Cream, 0.7f);
            _rippleT = 0f;
            _ripple.gameObject.SetActive(Application.isPlaying);
        }

        // ---------------------------------------------------------------- popup

        public void ShowPopup(Island island, IslandHerdSystem herds, int herd, int member)
        {
            if (!WatchRules.Allowed(WatchFeature.Watch)) return;
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
            if (!WatchRules.Allowed(WatchFeature.Watch)) return;
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
            _popupTimer = popupShowSeconds + popupFadeSeconds;
            _stillMarker = 0f;
            _shownState = _shownSize = _shownYoung = -1;
            SetActive(_popupFollow, canFollow);
            string origin = _popupHerds != null && _popupHerds.IsForeign(_popupKind) ? OriginLine(_popupKind) : "";
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
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera, out var local);
            float diameter = Mathf.Clamp(bodyPixels * 2.6f / CanvasScale, 46f, 240f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.5f);
            _marker.anchoredPosition = local;
            _marker.localScale = new Vector3(diameter / 56f, diameter / 56f * 0.62f, 1f);
            _markerGroup.alpha = alpha * (0.8f + 0.2f * pulse);
            return diameter * 0.62f;
        }

        void PlacePopup(Vector3 screen, float lift)
        {
            if (screen.z <= 0f) { _popupGroup.alpha = 0f; return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera, out var local);
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
        }

        // The camera leaves the island and orbits the herd; the island is not steered and does not sink meanwhile.
        public void FollowHerd(Island island, IslandHerdSystem herds, int herd) =>
            BeginWatch(WatchSubjects.OfHerd(island, herds, herd));

        // THE watch mode ("Tier beobachten"). Every way in — tapping an animal, a discovery toast, a journal card or
        // a photo-task cue — builds a WatchSubject and comes through here, so all of them end in exactly the same
        // state: menus closed, the game running, input and sinking held, the orbit rig on the subject at the chase
        // framing, and the creature's own card telling the player what is being watched.
        public bool BeginWatch(WatchSubject subject)
        {
            if (subject == null || !WatchRules.Allowed(WatchFeature.Watch)) return false;
            Resolve();
            if (chaseCamera == null) return false;
            if (_journalOpen) CloseJournal();
            if (_album.IsOpen) _album.Close();
            if (_photoActive) ExitPhotoMode();
            if (session != null && session.Current == GameSession.State.Paused) session.Resume();
            if (Following) ReturnToIsland();
            HidePopup();

            _watch = subject;
            _followIsland = subject.ground;
            _followHerds = subject.herds;
            _followHerd = subject.herd;
            if (_followHerds != null)
            {
                if (_followHerd < 0 || _followHerd >= _followHerds.HerdCount) { _watch = null; _followHerds = null; return false; }
                _followHerdCount = _followHerds.HerdCount;
                _followCenter = _followHerds.HerdCenter(_followHerd);
                _followKind = _followHerds.HerdKind(_followHerd);
                _focusSnap = true;
                StepHerdFocus(0f);
                _followRadius = TargetFollowRadius();
                _chipSize = _chipMood = -1;
                _chipTimer = 0f;
            }
            else
            {
                if (subject.focus == null || !subject.focus(out Vector3 focus)) { _watch = null; return false; }
                _followFocus = focus;
                _followRadius = subject.radius;
                if (_followChipText != null) _followChipText.text = subject.label;
            }
            UpdateFollow();
            if (!Following) return false;
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
            _stillIdle = 0f;
            ShowWatchPopup();
            if (subject.still) _stillMarker = popupShowSeconds + popupFadeSeconds;
            return true;
        }

        // A watched building gets the same outline ring on the ground a tapped animal gets under its card, for as
        // long as the card would stay.
        float _stillMarker, _stillIdle;

        void UpdateStillMarker()
        {
            if (_stillMarker <= 0f || _marker == null) return;
            bool popup = _popupHerds != null || _popupCritters != null;
            var cam = Camera.main;
            if (popup || !Following || _watch == null || !_watch.still || cam == null)
            {
                _stillMarker = 0f;
                if (!popup) SetActive(_marker.gameObject, false);
                return;
            }
            _stillMarker -= Time.unscaledDeltaTime;
            if (_stillMarker <= 0f)
            {
                SetActive(_marker.gameObject, false);
                return;
            }
            Vector3 screen = cam.WorldToScreenPoint(_followFocus);
            float px = (cam.WorldToScreenPoint(_followFocus + cam.transform.right * (_watch.radius * 0.45f)) - screen).magnitude;
            SetActive(_marker.gameObject, true);
            PlaceMarker(screen, px, Mathf.Clamp01(_stillMarker / Mathf.Max(0.01f, popupFadeSeconds)));
        }

        // The same card a tap on the creature gives, minus the follow button (it is already being followed): it
        // hangs under the animal while watching, so every entry point names what the camera went to.
        void ShowWatchPopup()
        {
            if (_watch == null) return;
            if (_followHerds != null)
            {
                if (_followHerd < 0 || _followHerd >= _followHerds.HerdCount || _followHerds.HerdSize(_followHerd) == 0) return;
                _popupIsland = _followIsland;
                _popupHerds = _followHerds;
                _popupCritters = null;
                _popupHerdIndex = _followHerd;
                _popupMember = 0;
                _popupKind = _followKind;
                OpenPopup(false);
                return;
            }
            var critters = _watch.critters;
            if (critters == null || _watch.critter < 0 || _watch.critter >= critters.CritterCount) return;
            _popupIsland = _watch.ground;
            _popupHerds = null;
            _popupCritters = critters;
            _popupHerdIndex = -1;
            _popupMember = _watch.critter;
            _popupKind = critters.KindOf(_watch.critter);
            OpenPopup(false);
        }

        // Watch the nearest example of a journal entry (a journal card or a discovery toast). Closes the journal and
        // the pause menu. False when there is none in reach right now; the news chip then says so.
        public bool Watch(int catalogIndex)
        {
            if (catalogIndex < 0 || catalogIndex >= CollectionCatalog.Count || !WatchRules.Allowed(WatchFeature.Watch)) return false;
            Resolve();
            if (player == null || chaseCamera == null) return false;
            var e = CollectionCatalog.At(catalogIndex);
            var subject = WatchSubjects.Find(e, player, player.PlanarPosition, flocks, fish, seaLife, ships);
            if (subject == null)
            {
                ShowNotice(e.name + " ist gerade nicht in der Nähe");
                return false;
            }
            return BeginWatch(subject);
        }

        // A one-line message in the news chip, outside the collection queue. A photo hint can be tapped: it takes the
        // camera to the creature it is about and opens photo mode.
        void ShowNotice(string text, bool photoHint = false)
        {
            _toasts.Dismiss();
            _noticeTimer = photoHint ? photoHintSeconds : noticeSeconds;
            _noticePhoto = photoHint;
            ShowNews(new CollectionToast { text = text, strong = photoHint, photo = photoHint, count = photoHint ? 1 : 0 });
            SetActive(_newsChip, true);
        }

        public void ReturnToIsland()
        {
            if (!Following) return;
            _watch = null;
            if (_stillMarker > 0f)
            {
                _stillMarker = 0f;
                if (_popupHerds == null && _popupCritters == null && _marker != null) SetActive(_marker.gameObject, false);
            }
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

        // Where watching starts and R returns to: looking down at watchPitch from the chase view's side (turned to the
        // clearest view with watchClearView), a herd at the distance that frames it with readable animals, anything
        // else at the chase framing's distance at followZoomLevel.
        void SetFollowHome()
        {
            float zoom = Mathf.Max(chaseCamera.zoomMin, followZoomLevel);
            var basis = _followIsland != null ? _followIsland : player;
            // With the fixed north view the chase camera no longer sits behind the island's heading, so the watch
            // orbit starts from the same basis - otherwise watching would swing the view to a different side.
            Vector3 back = chaseCamera != null && chaseCamera.ViewIsFixed ? -chaseCamera.ViewForward
                         : basis != null ? -basis.Forward : Vector3.back;
            IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, back, _followRadius, chaseCamera.referenceRadius,
                chaseCamera.zoomExponent, chaseCamera.height, chaseCamera.distanceBehind, zoom, out Vector3 pos, out _, out _);
            float yaw = 0f, pitch = 40f, dist = 4f;
            PhotoRig.OrbitOf(-pos, ref yaw, ref pitch, ref dist);
            if (Mathf.Abs(back.x) + Mathf.Abs(back.z) > 1e-4f) yaw = Mathf.Atan2(-back.x, -back.z) * Mathf.Rad2Deg;
            pitch = watchPitch;
            float frameRadius = Mathf.Max(0.5f, _followRadius);
            if (_followHerds != null)
            {
                var cam = Camera.main;
                frameRadius = Mathf.Max(0.5f, _herdSpread + 0.3f);
                dist = WatchFraming.Distance(frameRadius, _followHerds.BodyLength(_followHerd),
                    cam != null ? cam.fieldOfView : 60f, cam != null ? cam.aspect : 9f / 16f, watchFill, watchMinBodyPixels);
            }
            else if (_watch != null && _watch.still)
            {
                // A building: from the side at a lower angle, far enough to show the ground (or water) round it.
                var cam = Camera.main;
                pitch = stillPitch;
                dist = WatchFraming.Distance(frameRadius, 0f, cam != null ? cam.fieldOfView : 60f, cam != null ? cam.aspect : 9f / 16f, stillFill, 0f);
            }
            dist = Mathf.Clamp(dist, _rig.minDistance, _rig.maxDistance);
            if (watchClearView) yaw = ClearestYaw(yaw, pitch, dist, frameRadius);
            _rig.SetHome(yaw, pitch, dist);
        }

        // Scores YawCandidates directions around the subject: trees standing in the sight line (WatchFraming.Occlusion)
        // plus ground rising above it, a small penalty for turning away from the preferred side.
        float ClearestYaw(float preferred, float pitch, float dist, float radius)
        {
            Vector3 subject = SubjectPosition();
            float pr = Mathf.Clamp(pitch, 1f, 89f) * Mathf.Deg2Rad;
            float horizontal = dist * Mathf.Cos(pr), rise = Mathf.Tan(pr);
            int n = GatherOccluders(subject, horizontal + radius + 1f);
            float step = 360f / YawCandidates;
            for (int i = 0; i < YawCandidates; i++)
            {
                float yr = WatchFraming.CandidateYaw(preferred, i, step) * Mathf.Deg2Rad;
                Vector2 toCam = new Vector2(-Mathf.Sin(yr), -Mathf.Cos(yr));
                float cost = 0f;
                for (int k = 1; k <= 3; k++)
                {
                    float along = horizontal * k / 3.3f;
                    Vector2 xz = new Vector2(subject.x, subject.z) + toCam * along;
                    if (GroundHeight(xz) > subject.y + 0.15f + along * rise) cost += 1f;
                }
                _yawCost[i] = cost;
            }
            return WatchFraming.PickYaw(preferred, pitch, dist, subject, radius, _occluders, n, YawCandidates, 1.5f, _yawCost);
        }

        // Trees and palms within reach of the subject as (x, top height, z) in world space; one pass at the start of
        // watching, the buffer only ever grows.
        int GatherOccluders(Vector3 subject, float reach)
        {
            int n = 0;
            Vector2 s = new Vector2(subject.x, subject.z);
            var all = Island.All;
            for (int k = 0; k < all.Count; k++)
            {
                var island = all[k];
                if (island == null) continue;
                float r = reach + island.BoundingRadius;
                if ((island.PlanarPosition - s).sqrMagnitude > r * r) continue;
                if (!island.TryGetComponent(out IslandLifeSystem life) || !life.isActiveAndEnabled) continue;
                float baseY = island.transform.position.y;
                for (int i = 0; i < life.PlantCount; i++)
                {
                    if (!life.PlantIsTall(i) || life.PlantDyingOf(i)) continue;
                    Vector2 local = life.PlantPositionOf(i);
                    Vector2 w = island.ToWorld(local);
                    if ((w - s).sqrMagnitude > reach * reach) continue;
                    float top = baseY + Mathf.Max(0f, island.SampleHeight(local)) + TreeHeight * Mathf.Lerp(0.4f, 1f, life.PlantMaturityOf(i));
                    if (n == _occluders.Length) Array.Resize(ref _occluders, n * 2);
                    _occluders[n++] = new Vector3(w.x, top, w.y);
                }
            }
            return n;
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

        float TargetFollowRadius() => Mathf.Max(1.5f, _herdSpread + 1f);

        Vector3 HerdFocus()
        {
            var c = _focusLocal;
            return _followIsland.transform.TransformPoint(c.x, Mathf.Max(0f, _followIsland.SampleHeight(c)), c.y);
        }

        // Eases the watched point towards the middle of the animals in the island's frame (so the island's own drift
        // never lags); a birth, a death or a herd found again does not jump the camera.
        void StepHerdFocus(float dt)
        {
            int n = _followHerds.HerdSize(_followHerd);
            Vector2 mid = _followHerds.HerdCenter(_followHerd);
            if (n > 0)
            {
                Vector2 sum = Vector2.zero;
                for (int m = 0; m < n; m++)
                {
                    Vector3 p = _followHerds.AnimalLocalPosition(_followHerd, m);
                    sum += new Vector2(p.x, p.z);
                }
                mid = sum / n;
            }
            float spread = 0f;
            for (int m = 0; m < n; m++)
            {
                Vector3 p = _followHerds.AnimalLocalPosition(_followHerd, m);
                spread = Mathf.Max(spread, (new Vector2(p.x, p.z) - mid).magnitude);
            }
            _herdSpread = spread;
            if (_focusSnap)
            {
                _focusLocal = mid;
                _focusSnap = false;
            }
            else _focusLocal = Vector2.Lerp(_focusLocal, mid, 1f - Mathf.Exp(-watchFocusFollow * Mathf.Max(0f, dt)));
        }

        void UpdateFollow()
        {
            if (!Following) return;
            if (_followHerds == null)
            {
                if (!_watch.focus(out Vector3 f))
                {
                    // Sea life is recycled beyond its range from the player and critters leave: say so instead
                    // of silently cutting back to the island.
                    string gone = _watch.gone ?? _watch.label + " ist weitergezogen";
                    ReturnToIsland();
                    ShowNotice(gone);
                    return;
                }
                _followFocus = f;
                float odt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
                _followRadius = Mathf.Lerp(_followRadius, _watch.radius, 1f - Mathf.Exp(-1.5f * odt));
                return;
            }
            if (!ResolveFollowHerd()) { ReturnToIsland(); return; }
            // The framing radius (zoom limits) is eased so a herd that spreads out or huddles does not pump the camera.
            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
            StepHerdFocus(dt);
            _followFocus = HerdFocus();
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
            if (_followHerds == null)
            {
                if (_watch != null && _watch.focus != null && _watch.focus(out Vector3 f)) _followFocus = f;
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
            if (!WatchRules.Allowed(WatchFeature.DiscoveryRecord)) return;
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
            // A mode without the collection records nothing at all: an adventure run must not fill the cozy album.
            if (!WatchRules.Allowed(WatchFeature.DiscoveryRecord)) return;
            // The first scan of a run (new or loaded) is where it starts from, not news.
            bool first = !journal.Baselined;
            if (player != null) ScanPlayerIsland(journal, time);
            bool all = journal.RunAllSeen;
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
                    if (!all && island != player && herds.Tier == LifeTier.Near && (herds.SpeciesPresent & journal.RunUnseenLifeMask) != 0)
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
                    if (island != player && (journal.RunUnseenLifeMask & CollectionCatalog.PlantMask) != 0) NoteSeen(journal, life.PlantsPresent, time);
                }
                if (all || island == player || (journal.RunUnseenLifeMask & CollectionCatalog.CritterMask) == 0) continue;
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
                int sealSlots = seaLife.SealSlots;
                for (int i = 0; i < sealSlots; i++)
                    if (seaLife.SealActive(i) && seaLife.SealStateOf(i) != 0 && (seaLife.SealPosition(i) - focus).sqrMagnitude <= range2)
                        NoteSeen(journal, CollectionCatalog.IndexOf(SeaKind.Seal), time);
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
            if (_journalOpen || !WatchRules.Allowed(WatchFeature.Journal)) return;
            Resolve();
            _journalPausedSession = Application.isPlaying && session != null && session.Current == GameSession.State.Playing && session.Model.Pause();
            _journalOpen = true;
            _journalVersion = -1;
            _journalTimer = 0f;
            HidePopup();
            if (Application.isPlaying && Journal != null) RefreshPlantCounts(Journal);
            _tasksVersion = -1;
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
            var tasks = photoTasks ? PhotoTasks : null;
            int tv = tasks != null ? tasks.Version : -2;
            if (tv != _tasksVersion)
            {
                _tasksVersion = tv;
                _journalPanel.SetPhotoTasks(tasks, TaskThumb);
            }
            if (j.Version == _journalVersion && _journalTimer > 0f) return;
            _journalTimer = journalRefresh;
            _journalVersion = j.Version;
            _journalPanel.Fill(j, lifeBook ? LifeBook : null, IslandInfo());
        }

        // ---------------------------------------------------------------- photo tasks

        // The picture of a done task: loaded once from its file (or null when there is none).
        Texture TaskThumb(int task)
        {
            if (_taskThumbs.TryGetValue(task, out var tex)) return tex;
            var book = _photoTasks;
            tex = book != null && book.IsDone(task) ? PhotoLibrary.LoadTexture(book.ThumbPath(task)) : null;
            _taskThumbs[task] = tex;
            return tex;
        }

        void ClearTaskThumbs()
        {
            foreach (var t in _taskThumbs.Values) PhotoLibrary.Dispose(t);
            _taskThumbs.Clear();
        }

        // Which open photo tasks a picture taken now with this camera fulfils; they are marked done (with a square
        // crop of the saved photo around the subject as their picture) and announced. Returns how many.
        public int CheckPhotoTasks(in PhotoView view, string photoPath)
        {
            if (!photoTasks || !WatchRules.Allowed(WatchFeature.PhotoTaskRecord)) return 0;
            var book = PhotoTasks;
            if (book.OpenMask == 0) return 0;
            _subjects.Clear();
            PhotoSubjects.Gather(new Vector2(view.position.x, view.position.z), photoTaskHintRange * 2f, book.OpenMask, flocks, _subjects);
            int n = PhotoSubjects.Judge(view, _subjects, photoTaskMinSize, photoTaskMargin, _judgedTasks, _judgedCentres);
            if (n == 0) return 0;
            Texture2D photo = string.IsNullOrEmpty(photoPath) ? null : PhotoLibrary.LoadTexture(photoPath);
            var now = DateTime.Now;
            int done = 0;
            for (int i = 0; i < n; i++)
            {
                int task = _judgedTasks[i];
                if (!book.Complete(task, now)) continue;
                done++;
                _toasts.PhotoTaskDone(task);
                if (photo != null) StoreTaskThumb(book, task, photo, _judgedCentres[i]);
            }
            PhotoLibrary.Dispose(photo);
            if (done > 0 && Application.isPlaying) book.Save();
            return done;
        }

        void StoreTaskThumb(PhotoTaskBook book, int task, Texture2D photo, Vector2 centre)
        {
            string path = book.NewThumbPath(task);
            Texture2D thumb = null;
            try
            {
                thumb = PhotoSubjects.MakeThumb(photo, PhotoSubjects.CropAround(photo.width, photo.height, centre, 0.5f), 192);
                if (path != null)
                {
                    System.IO.Directory.CreateDirectory(book.ThumbDirectory);
                    System.IO.File.WriteAllBytes(path, thumb.EncodeToPNG());
                    book.SetThumb(task, System.IO.Path.GetFileName(path));
                }
                if (_taskThumbs.TryGetValue(task, out var old)) PhotoLibrary.Dispose(old);
                _taskThumbs[task] = thumb;
                thumb = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("WatchTools: photo task picture failed: " + e.Message);
            }
            finally
            {
                PhotoLibrary.Dispose(thumb);
            }
        }

        // The close-range cue: a small camera button over the nearest creature (in view, around the camera focus)
        // that is performing the move of an open photo task, and now and then a hint in the news chip. Rescans twice a
        // second; the button follows its creature every frame.
        // Lift of the icon above the creature, in canvas units.
        const float CueLift = 64f;

        void UpdatePhotoCue(bool visible, float dt)
        {
            if (_hintToastTimer > 0f && visible && !_photoActive) _hintToastTimer -= dt;
            var cam = Camera.main;
            if (!visible || !photoTasks || cam == null || !WatchRules.Allowed(WatchFeature.PhotoTaskCue) || PhotoTasks.OpenMask == 0)
            {
                _cueActive = false;
                SetActive(_cueButton, false);
                return;
            }
            var view = PhotoView.Of(cam);
            if (_cueActive && (!PhotoSubjects.Refresh(ref _cue) || !PhotoTasks.IsOpen(PhotoTaskCatalog.At(_cue.task).kind) || !OnScreen(view, _cue.world))) _cueActive = false;
            _cueTimer -= dt;
            if (!_cueActive && _cueTimer <= 0f)
            {
                _cueTimer = 0.5f;
                FindCue(view);
            }
            // Inside photo mode there is nothing left to do with it, so it steps aside instead of sitting there dead.
            SetActive(_cueButton, _cueActive && !_photoActive);
            if (!_cueActive) return;
            Vector3 screen = cam.WorldToScreenPoint(_cue.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera, out var local);
            float bob = Application.isPlaying ? Mathf.Sin(Time.unscaledTime * 3f) * 6f : 0f;
            PlaceCue(local + new Vector2(0f, CueLift + bob));

            if (_hintToastTimer <= 0f && !_photoActive && !_toasts.Showing && _noticeTimer <= 0f)
            {
                _hintToastTimer = photoTaskHintInterval;
                ShowNotice(PhotoTaskCatalog.At(_cue.task).hint, true);
            }
        }

        // The icon goes where it is told; the finger area keeps up with the size slider and the screen position is
        // remembered for the tap test.
        void PlaceCue(Vector2 local)
        {
            if (_cueRect == null) return;
            if (_cueRect.sizeDelta.x != cueSize) _cueRect.sizeDelta = new Vector2(cueSize, cueSize);
            _cueRect.anchoredPosition = local;
            float hit = CueHitSize(cueTapRadius);
            if (_cueHit != null && _cueHitSize != hit)
            {
                _cueHitSize = hit;
                _cueHit.Center(Vector2.zero, new Vector2(hit, hit));
            }
            // The canvas is an overlay, so a RectTransform's world position already is its screen position.
            _cueScreen = RectTransformUtility.WorldToScreenPoint(UiCamera, _cueRect.position);
        }

        // A square around the icon, never smaller than the 90 px a finger needs on a 1080p screen.
        public static float CueHitSize(float tapRadius) => Mathf.Max(90f, tapRadius * 2f);

        // Puts a cue up without the live scan, so tests and eval can drive the cue path in Edit Mode.
        public void SetPhotoCue(PhotoSubject subject)
        {
            _cue = subject;
            _cueActive = subject.task >= 0 && WatchRules.Allowed(WatchFeature.PhotoTaskCue)
                && (subject.herds != null || subject.critters != null || subject.flocks != null);
            SetActive(_cueButton, _cueActive && !_photoActive);
            var cam = Camera.main;
            if (!_cueActive || cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(_cue.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera, out var local);
            PlaceCue(local + new Vector2(0f, CueLift));
        }

        // Whether a press at that screen point counts as a tap on the cue. Measured against where the icon is right
        // now rather than through Unity's button, which needs press AND release on the same rect — a rect that
        // travels with a dashing hare and a drifting island, which is why the cue used to do nothing.
        public bool CueContains(Vector2 screenPos)
        {
            if (!_cueActive || _cueButton == null || !_cueButton.activeSelf) return false;
            // Never tighter than the square that is drawn, and never below the 1080p pixel size on a small screen.
            float reach = CueHitSize(cueTapRadius) * 0.5f * Mathf.Max(PixelsPerUnit, CanvasScale);
            Vector2 d = screenPos - _cueScreen;
            return Mathf.Abs(d.x) <= reach && Mathf.Abs(d.y) <= reach;
        }

        static bool OnScreen(in PhotoView view, Vector3 world) =>
            view.Project(world, out Vector2 vp, out _) && vp.x > 0.03f && vp.x < 0.97f && vp.y > 0.03f && vp.y < 0.9f;

        void FindCue(in PhotoView view)
        {
            _subjects.Clear();
            Vector3 focus = _driving ? SubjectPosition() : player != null ? player.transform.position : view.position;
            PhotoSubjects.Gather(new Vector2(focus.x, focus.z), photoTaskHintRange, PhotoTasks.OpenMask, flocks, _subjects);
            float best = float.MaxValue;
            for (int i = 0; i < _subjects.Count; i++)
            {
                var sub = _subjects[i];
                if (!OnScreen(view, sub.world)) continue;
                float d = (sub.world - view.position).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                _cue = sub;
                _cueActive = true;
            }
        }

        // The cue (or its hint) was tapped: the creature goes into the one watch mode — a herd, a critter, a flock,
        // whatever the cue is about — and photo mode opens on it from there.
        public void PhotographCue()
        {
            if (_photoActive) return;
            if (_cueActive && PhotoSubjects.Refresh(ref _cue)) BeginWatch(WatchSubjects.OfCue(_cue));
            EnterPhotoMode();
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
            if (_photoActive || !WatchRules.Allowed(WatchFeature.PhotoMode)) return;
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
            SetActive(_cueButton, false);
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
            SetActive(_cueButton, _cueActive);
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
            int tasksDone = 0;
            var cam = Camera.main;
            // The camera as it was for this very frame: the photo is judged against the picture that was taken.
            var view = cam != null ? PhotoView.Of(cam) : default;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                var entry = PhotoLibrary.Save(shot, PhotoDirectory, DateTime.Now);
                ok = true;
                if (cam != null) tasksDone = CheckPhotoTasks(view, entry.path);
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
            SetToast(!ok ? "Speichern fehlgeschlagen" : tasksDone == 1 ? "Fotoaufgabe erfüllt!" : tasksDone > 1 ? tasksDone + " Fotoaufgaben erfüllt!" : "Foto gespeichert");
            _toastTimer = tasksDone > 0 ? ToastSeconds * 1.5f : ToastSeconds;
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
            _rig.pivotLift = Following && _watch != null && _watch.still ? _watch.lift : 0f;
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
            _lift = 0f;
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
            var all = Island.All;
            for (int k = 0; k < all.Count; k++)
            {
                var island = all[k];
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
            var all = Island.All;
            for (int k = 0; k < all.Count; k++)
            {
                var island = all[k];
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
            // While watching, the view tilts up so the subject sits in the middle of the free picture below the
            // return button and the herd chip, not behind them; photo mode (no buttons on top) looks straight at it.
            float liftTarget = Following && !_photoActive && watchCenterBelowHud
                ? WatchFraming.LiftDegrees(cam.fieldOfView, WatchFraming.FreeBandCenter(HudCoveredFraction(), 0.05f)) : 0f;
            _lift = Mathf.Lerp(_lift, liftTarget, 1f - Mathf.Exp(-4f * dt));
            if (Mathf.Abs(_lift) > 1e-3f) rot *= Quaternion.Euler(-_lift, 0f, 0f);
            if (_rotBlend < 1f)
            {
                _rotBlend = Mathf.Min(1f, _rotBlend + dt / 0.5f);
                rot = Quaternion.Slerp(_rotBlendFrom, rot, Mathf.SmoothStep(0f, 1f, _rotBlend));
            }
            cam.transform.SetPositionAndRotation(pos, rot);
            if (chaseCamera != null) chaseCamera.SuspendedClose = close;
            // The orbit camera knows exactly how close it stands to what it watches; the vegetation calms its wind
            // by that distance instead of guessing from the view ray.
            IslandLifeSystem.ReportViewDistance((pos - pivot).magnitude);
        }

        // Share of the picture height the follow controls cover from the top (return button, herd chip, hint).
        float HudCoveredFraction()
        {
            float h = _root != null ? _root.rect.height : 0f;
            return h > 1f ? Mathf.Clamp01(FollowUiBottom / h) : 0.36f;
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
            else _hintRect.TopCenter(new Vector2(0f, -616f), size);
        }

        // ---------------------------------------------------------------- editor preview

        void EditorPreviewUpdate()
        {
            var preview = editorPreview;
            bool album = preview == EditorPreview.Album || preview == EditorPreview.AlbumPhoto;
            // An edit-mode frame of the adventure mode shows none of this either - only the album, which belongs to
            // no run.
            if (!WatchRules.Enabled && !album) preview = EditorPreview.None;
            bool pop = preview == EditorPreview.Popup, ph = preview == EditorPreview.Photo;
            bool jr = preview == EditorPreview.Journal || preview == EditorPreview.JournalTasks;
            // A popup or a followed herd staged from eval stays up (and live) so it can be looked at in Edit Mode.
            // Photo mode entered from eval counts too; the camera only moves when eval calls StepCamera.
            bool staged = preview == EditorPreview.None && (Following || _photoActive || _cueActive || _popupHerds != null || _popupCritters != null);
            if (staged)
            {
                UpdateFollow();
                UpdatePopup();
                SetActive(_followButton, Following && !_photoActive);
                SetActive(_followChip, Following && !_photoActive);
                SetActive(_bookButton, !_photoActive);
                SetActive(_cameraButton, !_photoActive);
                SetActive(_photo, _photoActive);
                SetActive(_cueButton, _cueActive && !_photoActive);
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
            if (_albumPreview != preview)
            {
                _albumPreview = preview;
                _album.Close();
                if (album) _album.OpenSample(preview == EditorPreview.AlbumPhoto);
            }
            if (album) _album.Tick();
            SetActive(_newsChip, pop);
            SetActive(_cueButton, pop);
            if (pop)
            {
                PlaceCue(new Vector2(-250f, 120f));
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
                if (_sampleTasks == null) _sampleTasks = SampleTasks();
                if (_journalPanel.PhotoTasks != _sampleTasks) _journalPanel.SetPhotoTasks(_sampleTasks, SampleThumb);
                _journalPanel.Fill(_sampleJournal, _sampleBook, SampleIsland);
                if (_journalPreviewShown != preview) _journalPanel.SetTab(preview == EditorPreview.JournalTasks ? JournalPanel.TasksTab : 0);
            }
            _journalPreviewShown = preview;
            if (ph) SetToast("Foto gespeichert");
        }

        static readonly JournalIslandInfo SampleIsland = new JournalIslandInfo
        {
            biome = "Biom: Gemäßigt", stage = "Dorf", animals = 48, herds = 9, plants = 812, trees = 96, absorbed = 4, foreignKinds = 6,
            buildings = 14, settlers = 16, villages = 1, buildingLine = "Lagerfeuer 1  ·  Hütte 5  ·  Brunnen 1\nGarten 2  ·  Steg 1  ·  Haus 3\nWindmühle 1",
        };

        readonly LifeBook _sampleBook = new();
        EditorPreview _journalPreviewShown = EditorPreview.None;

        // Edit Mode preview only: a few tasks done, with a soft two-tone square in the species colour as their picture.
        static PhotoTaskBook SampleTasks()
        {
            var book = new PhotoTaskBook(null);
            var when = new DateTime(2026, 9, 22, 18, 30, 0);
            foreach (var k in new[] { LifeKind.Hare, LifeKind.Sheep, LifeKind.Flamingo, LifeKind.Crab, LifeKind.Butterfly })
                book.Complete(PhotoTaskCatalog.IndexOf(k), when);
            return book;
        }

        Texture SampleThumb(int task)
        {
            if (_taskThumbs.TryGetValue(task, out var tex)) return tex;
            const int n = 48;
            tex = new Texture2D(n, n, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            Color sw = JournalPanel.SwatchOf(CollectionCatalog.At(PhotoTaskCatalog.At(task).entry));
            Color sky = new Color(0.55f, 0.78f, 0.9f), ground = new Color(0.42f, 0.62f, 0.36f);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color c = y > n * 0.55f ? Color.Lerp(sky, Color.white, (y - n * 0.55f) / n) : ground;
                    float d = new Vector2(x - n * 0.5f, (y - n * 0.45f) * 1.4f).magnitude;
                    px[y * n + x] = d < n * 0.22f ? sw : c;
                }
            tex.SetPixels(px);
            tex.Apply(false);
            _taskThumbs[task] = tex;
            return tex;
        }

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
            _cueButton = BuildPhotoCue(_root);
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
            _cueButton.SetActive(false);
            _cueActive = false;
        }

        // Sits above the creature, over the creature card (a button must not hide under a label) but under the
        // return button, the chip and the full-screen panels. The drawn button stays small and light; a
        // transparent square around it takes the tap, so the finger has at least 90 px to aim at.
        GameObject BuildPhotoCue(RectTransform root)
        {
            var b = UiStyle.IconButton(root, "PhotoTaskCue", cueSize, UiIcon.Camera, PhotographCue);
            _cueRect = (RectTransform)b.transform;
            _cueRect.Center(Vector2.zero, new Vector2(cueSize, cueSize));
            _cueHitSize = CueHitSize(cueTapRadius);
            _cueHit = UiStyle.Shape(b.transform, "Hit", null, UiStyle.WithAlpha(Color.white, 0f), true).rectTransform;
            _cueHit.Center(Vector2.zero, new Vector2(_cueHitSize, _cueHitSize));
            if (_popup != null) _cueRect.SetSiblingIndex(_popup.transform.GetSiblingIndex() + 1);
            return b.gameObject;
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
            // Only the outline (owner, 2026-09-23: the filled, pulsing disc under the animal was too loud).
            UiStyle.Shape(marker, "Ring", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Sand, 0.75f)).rectTransform.Stretch();
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
            ((RectTransform)b.transform).TopCenter(new Vector2(0f, -400f), new Vector2(680f, 120f));
            return b.gameObject;
        }

        GameObject BuildFollowChip(RectTransform root)
        {
            var chip = UiStyle.Chip(root, "FollowChip", "", new Vector2(680f, 68f), out _followChipText);
            chip.TopCenter(new Vector2(0f, -536f), new Vector2(680f, 68f));
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
            _hintRect.TopCenter(new Vector2(0f, -616f), new Vector2(820f, 60f));
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
