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

        public void Release()
        {
            _stickId = int.MinValue;
            StickDeflected = false;
            Move = Vector2.zero;
            _pinching = false;
            _pendingPinch = 1f;
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
                return;
            }
            CollectPointers();
            UpdatePinch();
            UpdateStick();
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
                if (IsOverUi(p.id)) continue;
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

        static bool IsOverUi(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
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
            _canvas = UiStyle.Canvas(transform, CanvasName, sortingOrder, false, true, out _);
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
}
