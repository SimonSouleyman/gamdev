using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Drift.UI
{
    [ExecuteAlways]
    public class TouchControls : MonoBehaviour, IMoveInputSource
    {
        const string CanvasName = "TouchCanvas";

        public bool forceShowTouch;
        public bool inputEnabled = true;
        // Off while the tilt steering drives: the thumbstick disappears and every tap counts as a tap again,
        // but the two-finger pinch zoom keeps working.
        public bool stickEnabled = true;
        public bool editorPreview;
        public float stickRadius = 150f;
        public float knobRadius = 70f;
        [Range(0f, 0.5f)] public float deadZone = 0.15f;
        [Range(0.2f, 1f)] public float stickZoneWidth = 0.5f;
        public float pinchSensitivity = 1f;
        public int sortingOrder = 20;
        public Color baseColor = new Color(0.99f, 0.96f, 0.88f, 0.16f);
        public Color knobColor = new Color(0.99f, 0.96f, 0.88f, 0.62f);

        struct Pointer
        {
            public int id;
            public Vector2 pos;
            public bool began;
        }

        readonly List<Pointer> _pointers = new List<Pointer>(10);
        Canvas _canvas;
        RectTransform _canvasRect, _base, _knob;
        int _stickId = int.MinValue;
        Vector2 _stickOrigin;
        bool _pinching;
        float _pinchDist;
        float _pendingPinch = 1f;
        int _lookId = int.MinValue;
        Vector2 _lookLast, _lookDelta;

        public Vector2 Move { get; private set; }
        public bool StickActive => _stickId != int.MinValue;
        // For tap detection elsewhere: a press only counts as steering once the pointer the stick captured has
        // left the dead zone. Starting in the stick half alone does not make it a steer.
        public bool IsPointerOnStick(int pointerId) => StickActive && _stickId == pointerId;
        public bool StickDeflected { get; private set; }
        public bool IsPinching => _pinching;
        public bool Available => forceShowTouch || (Touchscreen.current != null && InputMode.TouchPreferred);

        public float ConsumePinchFactor()
        {
            float f = _pendingPinch;
            _pendingPinch = 1f;
            return f;
        }

        // The fly-over over the finished Pangäa reads a drag as "look around". Only the movement of a finger
        // that is not on the stick is reported; nothing is swallowed, so tapping an animal keeps working.
        public bool lookEnabled;

        public Vector2 ConsumeLookDelta()
        {
            Vector2 d = _lookDelta;
            _lookDelta = Vector2.zero;
            return d;
        }

        // Map-style gestures for the fly-over over the finished Pangäa: grab-pan, pinch, twist, two-finger tilt and
        // taps (TouchGestures). Fingers that went down on a button are left out; nothing is swallowed, so WatchTools
        // still sees its taps.
        public bool gesturesEnabled;
        readonly TouchGestures _gestures = new TouchGestures();
        readonly int[] _uiFingers = new int[10];
        int _uiFingerCount;
        int _quickTapId = int.MinValue;

        public TouchGestures Gestures => _gestures;

        public TouchGesture ConsumeGesture() => _gestures.Consume();

        public void Release()
        {
            _stickId = int.MinValue;
            StickDeflected = false;
            Move = Vector2.zero;
            _pinching = false;
            _pendingPinch = 1f;
            _lookId = int.MinValue;
            _lookDelta = Vector2.zero;
            _gestures.Reset();
            _uiFingerCount = 0;
            SetVisible(false);
        }

        void OnEnable()
        {
            Build();
        }

        void OnDisable()
        {
            Release();
        }

        void Update()
        {
            if (_canvas == null) return;
            if (!Application.isPlaying)
            {
                SetVisible(editorPreview);
                if (editorPreview) PlacePreview();
                return;
            }
            if (!inputEnabled || !Available)
            {
                if (StickActive || _pinching) Release();
                else SetVisible(false);
                if (_gestures.Active) _gestures.Reset();
                return;
            }
            CollectPointers();
            UpdatePinch();
            if (stickEnabled) UpdateStick();
            else if (StickActive || Move != Vector2.zero) ReleaseStick();
            if (lookEnabled) UpdateLook();
            else if (_lookId != int.MinValue || _lookDelta != Vector2.zero) { _lookId = int.MinValue; _lookDelta = Vector2.zero; }
            if (gesturesEnabled) UpdateGestures();
            else if (_gestures.Active) _gestures.Reset();
        }

        void UpdateGestures()
        {
            // Forget UI fingers that were lifted, remember new ones that went down on a button.
            for (int k = _uiFingerCount - 1; k >= 0; k--)
            {
                bool present = false;
                for (int i = 0; i < _pointers.Count; i++) if (_pointers[i].id == _uiFingers[k]) { present = true; break; }
                if (!present) _uiFingers[k] = _uiFingers[--_uiFingerCount];
            }
            int n = 0, idA = 0, idB = 0;
            Vector2 a = default, b = default;
            for (int i = 0; i < _pointers.Count; i++)
            {
                var p = _pointers[i];
                if (p.began && _uiFingerCount < _uiFingers.Length && !IsUiFinger(p.id) && OverGraphic(p.pos)) _uiFingers[_uiFingerCount++] = p.id;
                if (IsUiFinger(p.id)) continue;
                if (n == 0) { idA = p.id; a = p.pos; }
                else if (n == 1) { idB = p.id; b = p.pos; }
                n++;
            }
            float px = TouchGestures.PixelScale(Screen.width, Screen.height);
            _gestures.Feed(n, idA, a, idB, b, Time.unscaledTime, px);
            CatchQuickTap(px);
        }

        bool IsUiFinger(int id)
        {
            for (int k = 0; k < _uiFingerCount; k++) if (_uiFingers[k] == id) return true;
            return false;
        }

        // A tap can begin and end between two frames (the Began phase is never seen): the lone finger's Ended phase
        // still carries where it started.
        void CatchQuickTap(float px)
        {
            var ts = Touchscreen.current;
            if (ts == null || _pointers.Count > 0 || _gestures.Active) return;
            var t = ts.primaryTouch;
            if (t.phase.ReadValue() != TouchPhase.Ended) return;
            int id = t.touchId.ReadValue();
            if (id == _quickTapId || _gestures.SawFinger(id)) return;
            _quickTapId = id;
            Vector2 pos = t.position.ReadValue(), start = t.startPosition.ReadValue();
            if (OverGraphic(pos)) return;
            _gestures.QuickTap(id, start, pos, px);
        }

        // The look finger: the first one that starts outside the stick and off the UI. Two fingers are a pinch,
        // never a look.
        void UpdateLook()
        {
            if (_pinching) { _lookId = int.MinValue; return; }
            if (_lookId != int.MinValue)
            {
                for (int i = 0; i < _pointers.Count; i++)
                    if (_pointers[i].id == _lookId)
                    {
                        _lookDelta += _pointers[i].pos - _lookLast;
                        _lookLast = _pointers[i].pos;
                        return;
                    }
                _lookId = int.MinValue;
                return;
            }
            for (int i = 0; i < _pointers.Count; i++)
            {
                var p = _pointers[i];
                if (!p.began || IsPointerOnStick(p.id)) continue;
                if (OverGraphic(p.pos)) continue;
                _lookId = p.id;
                _lookLast = p.pos;
                break;
            }
        }

        void ReleaseStick()
        {
            _stickId = int.MinValue;
            StickDeflected = false;
            Move = Vector2.zero;
            SetVisible(false);
        }

        void CollectPointers()
        {
            _pointers.Clear();
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    var phase = t.phase.ReadValue();
                    if (phase != TouchPhase.Began && phase != TouchPhase.Moved && phase != TouchPhase.Stationary) continue;
                    _pointers.Add(new Pointer { id = t.touchId.ReadValue(), pos = t.position.ReadValue(), began = phase == TouchPhase.Began });
                }
            }
            else if (forceShowTouch && Mouse.current != null)
            {
                var m = Mouse.current;
                if (m.leftButton.isPressed)
                    _pointers.Add(new Pointer { id = -1, pos = m.position.ReadValue(), began = m.leftButton.wasPressedThisFrame });
            }
        }

        void UpdatePinch()
        {
            if (_pointers.Count != 2)
            {
                _pinching = false;
                return;
            }
            float d = Vector2.Distance(_pointers[0].pos, _pointers[1].pos);
            if (_pinching && _pinchDist > 1f && d > 1f)
                _pendingPinch *= Mathf.Pow(_pinchDist / d, pinchSensitivity);
            _pinchDist = d;
            _pinching = true;
        }

        void UpdateStick()
        {
            if (StickActive)
            {
                int idx = -1;
                for (int i = 0; i < _pointers.Count; i++) if (_pointers[i].id == _stickId) { idx = i; break; }
                if (idx < 0)
                {
                    _stickId = int.MinValue;
                    StickDeflected = false;
                    Move = Vector2.zero;
                    SetVisible(false);
                    return;
                }
                if (_pinching)
                {
                    Move = Vector2.zero;
                    PlaceKnob(_stickOrigin);
                    return;
                }
                Vector2 delta = _pointers[idx].pos - _stickOrigin;
                float radiusPx = stickRadius * ScaleFactor;
                float mag = delta.magnitude;
                Vector2 dir = mag > 1e-3f ? delta / mag : Vector2.zero;
                float norm = Mathf.Clamp01(mag / radiusPx);
                float scaled = norm <= deadZone ? 0f : (norm - deadZone) / (1f - deadZone);
                Move = dir * scaled;
                if (scaled > 0f) StickDeflected = true;
                PlaceKnob(_stickOrigin + dir * Mathf.Min(mag, radiusPx));
                return;
            }

            for (int i = 0; i < _pointers.Count; i++)
            {
                var p = _pointers[i];
                if (!p.began) continue;
                if (p.pos.x > Screen.width * stickZoneWidth) continue;
                // Only a fresh raycast: IsPointerOverGameObject answers for the previous UI update, so after a tap on a
                // button (Tilda's "Weiter") it still said "over UI" for the next touch of that finger id, and the stick
                // refused the first touch (owner, first APK test: "hängt, erst nach mehrmaligem Probieren").
                if (OverGraphic(p.pos)) continue;
                _stickId = p.id;
                StickDeflected = false;
                _stickOrigin = p.pos;
                Move = Vector2.zero;
                SetVisible(true);
                PlaceBase(p.pos);
                PlaceKnob(p.pos);
                break;
            }
        }

        // A fresh UI raycast at this screen point: true when a raycastable graphic (button) is under it.
        static readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        static bool OverGraphic(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            _uiHits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screen }, _uiHits);
            for (int i = 0; i < _uiHits.Count; i++)
            {
                var go = _uiHits[i].gameObject;
                if (go != null && go.GetComponentInParent<TouchControls>() == null) return true;
            }
            return false;
        }

        float ScaleFactor => _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

        void PlaceBase(Vector2 screen)
        {
            if (_base == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            _base.anchoredPosition = local;
        }

        void PlaceKnob(Vector2 screen)
        {
            if (_knob == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            _knob.anchoredPosition = local;
        }

        void PlacePreview()
        {
            if (_canvasRect == null) return;
            var size = _canvasRect.rect.size;
            var p = new Vector2(-size.x * 0.5f + 260f, -size.y * 0.5f + 360f);
            _base.anchoredPosition = p;
            _knob.anchoredPosition = p + new Vector2(40f, 30f);
        }

        void SetVisible(bool on)
        {
            if (_base != null && _base.gameObject.activeSelf != on) _base.gameObject.SetActive(on);
            if (_knob != null && _knob.gameObject.activeSelf != on) _knob.gameObject.SetActive(on);
        }

        void Build()
        {
            UiStyle.DestroyChildrenNamed(transform, CanvasName);
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, false, out _);
            _canvasRect = (RectTransform)_canvas.transform;

            _base = Disc(_canvas.transform, "StickBase", stickRadius * 2f, baseColor, 1.3f, 0.5f);
            _knob = Disc(_canvas.transform, "StickKnob", knobRadius * 2f, knobColor, 1.55f, 0.9f);
            SetVisible(false);
        }

        // Translucent disc on a blurred shadow; nothing here takes raycasts, the stick reads the pointers itself.
        static RectTransform Disc(Transform parent, string name, float size, Color color, float shadowScale, float shadowStrength)
        {
            var rt = UiStyle.Rect(parent, name).Center(Vector2.zero, new Vector2(size, size));
            var shadow = UiStyle.Shape(rt, "Shadow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Shadow, UiStyle.Shadow.a * shadowStrength));
            shadow.rectTransform.Center(new Vector2(0f, -size * 0.04f), new Vector2(size, size) * shadowScale);
            UiStyle.Shape(rt, "Body", UiSprites.Circle, color).rectTransform.Stretch();
            UiStyle.Shape(rt, "Rim", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Cream, Mathf.Min(1f, color.a * 1.6f))).rectTransform.Stretch();
            var sheen = UiStyle.Shape(rt, "Sheen", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Cream, color.a * 0.35f));
            sheen.rectTransform.Center(new Vector2(0f, size * 0.12f), new Vector2(size, size) * 0.62f);
            return rt;
        }
    }

    // What the fingers did since the last read (TouchGestures.Consume). Positions are screen pixels, y up.
    public struct TouchGesture
    {
        // Fingers on the world right now.
        public int fingers;
        // A finger went down since the last read.
        public bool touched;
        // Every finger was lifted since the last read.
        public bool released;
        // One finger dragged the ground from panFrom to panTo.
        public bool pan;
        public Vector2 panFrom, panTo;
        // Two fingers: midpoint from midFrom to midTo, spread factor (> 1 = apart), turn (counter-clockwise).
        public bool twoFinger;
        public Vector2 midFrom, midTo;
        public float scale, twistDeg;
        // Both fingers up (+) or down the screen together.
        public float tiltPixels;
        public bool tap;
        public Vector2 tapPos;
    }

    // Map-app gesture recognition from raw finger positions, pure so it can be tested without a touchscreen.
    // One finger: a drag (after a small slop) or a tap. Two fingers first sit in a dead zone and then commit to
    // either a TILT (both move up or down together, side by side) or a TRANSFORM (midpoint pan, plus pinch and twist
    // that each unlock on their own threshold, so the jitter of a pinch never turns the view and a twist never
    // zooms by accident). Thresholds are in pixels of a 1080-wide portrait screen (PixelScale).
    public sealed class TouchGestures
    {
        // The same limits as WatchTools' tap gate, so a tap that opens an animal's card also flies to it.
        public float tapSlop = 30f;
        public float tapSeconds = 0.6f;
        public float panSlop = 12f;
        // Two fingers: spread (log) and turn (degrees) that commit to a transform, the same once the other one is
        // already on, and the vertical travel that commits to a tilt.
        public float zoomStart = 0.06f, zoomLate = 0.14f;
        public float twistStart = 10f, twistLate = 18f;
        public float tiltStart = 22f;
        public float twoPanStart = 30f;

        enum Two { None, Undecided, Transform, Tilt }

        TouchGesture _acc;
        int _count;
        // One finger.
        int _id = int.MinValue;
        Vector2 _start, _last;
        float _startTime;
        bool _panning, _tapOk;
        // Two fingers.
        Two _two;
        int _a = int.MinValue, _b = int.MinValue;
        Vector2 _a0, _b0, _aLast, _bLast;
        bool _zoomOn, _twistOn;
        float _accLog, _accTwist;
        readonly int[] _seen = new int[4];
        int _seenCursor;

        public TouchGestures() { Reset(); }

        public bool Active => _count > 0;
        public int Fingers => _count;
        public bool Tilting => _two == Two.Tilt;
        public bool Transforming => _two == Two.Transform;
        public bool Zooming => _two == Two.Transform && _zoomOn;
        public bool Twisting => _two == Two.Transform && _twistOn;

        // Thresholds are tuned for 1080 px on the short side.
        public static float PixelScale(float width, float height) => Mathf.Max(0.25f, Mathf.Min(width, height) / 1080f);

        public void Reset()
        {
            _acc = new TouchGesture { scale = 1f };
            _count = 0;
            _id = int.MinValue;
            _two = Two.None;
            _a = _b = int.MinValue;
            _panning = _tapOk = false;
            for (int i = 0; i < _seen.Length; i++) _seen[i] = int.MinValue;
        }

        public TouchGesture Consume()
        {
            var g = _acc;
            g.fingers = _count;
            _acc = new TouchGesture { scale = 1f };
            return g;
        }

        public bool SawFinger(int id)
        {
            for (int i = 0; i < _seen.Length; i++) if (_seen[i] == id) return true;
            return id == _id || id == _a || id == _b;
        }

        // A finger that came and went between two frames.
        public void QuickTap(int id, Vector2 start, Vector2 end, float px)
        {
            Remember(id);
            if ((end - start).magnitude > tapSlop * px) return;
            _acc.touched = true;
            _acc.released = true;
            _acc.tap = true;
            _acc.tapPos = end;
        }

        void Remember(int id)
        {
            if (id == int.MinValue || SawFinger(id)) return;
            _seen[_seenCursor] = id;
            _seenCursor = (_seenCursor + 1) % _seen.Length;
        }

        // count fingers on the world this frame; the first two are (idA, a) and (idB, b).
        public void Feed(int count, int idA, Vector2 a, int idB, Vector2 b, float time, float px)
        {
            px = Mathf.Max(0.25f, px);
            if (count <= 0)
            {
                if (_id != int.MinValue && _tapOk && time - _startTime <= tapSeconds && (_last - _start).magnitude <= tapSlop * px)
                {
                    _acc.tap = true;
                    _acc.tapPos = _last;
                }
                if (_count > 0) _acc.released = true;
                _id = int.MinValue;
                _two = Two.None;
                _a = _b = int.MinValue;
                _count = 0;
                return;
            }
            if (_count == 0) _acc.touched = true;

            if (count == 1)
            {
                Remember(idA);
                if (_two != Two.None)
                {
                    // One of two fingers lifted: the other one carries on as a drag from where it is, never as a tap.
                    _two = Two.None;
                    _a = _b = int.MinValue;
                    _id = idA;
                    _start = _last = a;
                    _startTime = time;
                    _tapOk = false;
                    _panning = true;
                }
                else if (_id != idA)
                {
                    if (_id != int.MinValue) _acc.touched = true;
                    _id = idA;
                    _start = _last = a;
                    _startTime = time;
                    _tapOk = _count == 0;
                    _panning = false;
                }
                else
                {
                    float moved = (a - _start).magnitude;
                    // _last follows the finger even inside the slop, so a tap reports where the finger came up.
                    if (moved > tapSlop * px || time - _startTime > tapSeconds) _tapOk = false;
                    if (!_panning && moved > panSlop * px) _panning = true;
                    if (_panning && (a - _last).sqrMagnitude > 1e-8f) AddPan(_last, a);
                    _last = a;
                }
                _count = 1;
                return;
            }

            Remember(idA);
            Remember(idB);
            _count = count;
            _tapOk = false;
            _id = int.MinValue;
            bool same = (idA == _a && idB == _b) || (idA == _b && idB == _a);
            if (_two == Two.None || !same)
            {
                _two = Two.Undecided;
                _a = idA;
                _b = idB;
                _a0 = _aLast = a;
                _b0 = _bLast = b;
                _zoomOn = _twistOn = false;
                _accLog = _accTwist = 0f;
                return;
            }
            if (idA == _b)
            {
                var t = a; a = b; b = t;
            }

            float dPrev = Mathf.Max(1f, (_bLast - _aLast).magnitude), dNow = Mathf.Max(1f, (b - a).magnitude);
            Vector2 midPrev = (_aLast + _bLast) * 0.5f, midNow = (a + b) * 0.5f;
            float angPrev = Angle(_bLast - _aLast), angNow = Angle(b - a);

            if (_two == Two.Undecided)
            {
                float d0 = Mathf.Max(1f, (_b0 - _a0).magnitude);
                float logS = Mathf.Log(dNow / d0);
                float turn = Mathf.DeltaAngle(Angle(_b0 - _a0), angNow);
                Vector2 da = a - _a0, db = b - _b0, dm = midNow - (_a0 + _b0) * 0.5f;
                Vector2 gap0 = _b0 - _a0;
                bool sideBySide = Mathf.Abs(gap0.y) < Mathf.Abs(gap0.x);
                bool parallel = da.y * db.y > 0f && Mathf.Abs(da.x) + Mathf.Abs(db.x) < 0.7f * (Mathf.Abs(da.y) + Mathf.Abs(db.y));
                bool tiltLike = sideBySide && parallel && Mathf.Abs(logS) < zoomStart && Mathf.Abs(turn) < twistStart;
                if (tiltLike && Mathf.Min(Mathf.Abs(da.y), Mathf.Abs(db.y)) > tiltStart * px) _two = Two.Tilt;
                else if (Mathf.Abs(logS) > zoomStart) { _two = Two.Transform; _zoomOn = true; }
                else if (Mathf.Abs(turn) > twistStart) { _two = Two.Transform; _twistOn = true; }
                else if (!tiltLike && dm.magnitude > twoPanStart * px) _two = Two.Transform;
                // The dead zone is not replayed: the gesture starts from here, so nothing jumps.
                _aLast = a;
                _bLast = b;
                return;
            }

            if (_two == Two.Tilt) _acc.tiltPixels += midNow.y - midPrev.y;
            else
            {
                float dLog = Mathf.Log(dNow / dPrev), dTurn = Mathf.DeltaAngle(angPrev, angNow);
                _accLog += dLog;
                _accTwist += dTurn;
                if (!_zoomOn && Mathf.Abs(_accLog) > (_twistOn ? zoomLate : zoomStart)) { _zoomOn = true; dLog = 0f; }
                if (!_twistOn && Mathf.Abs(_accTwist) > (_zoomOn ? twistLate : twistStart)) { _twistOn = true; dTurn = 0f; }
                if (!_acc.twoFinger)
                {
                    _acc.twoFinger = true;
                    _acc.midFrom = midPrev;
                }
                _acc.midTo = midNow;
                if (_zoomOn) _acc.scale *= Mathf.Exp(dLog);
                if (_twistOn) _acc.twistDeg += dTurn;
            }
            _aLast = a;
            _bLast = b;
        }

        void AddPan(Vector2 from, Vector2 to)
        {
            if (!_acc.pan)
            {
                _acc.pan = true;
                _acc.panFrom = from;
            }
            _acc.panTo = to;
        }

        static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    }
}
