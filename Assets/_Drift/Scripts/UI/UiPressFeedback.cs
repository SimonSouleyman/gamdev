using UnityEngine;
using UnityEngine.EventSystems;

namespace Drift.UI
{
    // Candy press: while held the control squashes (wider, flatter) and sinks into its lip; on release a soft
    // spring lets it bounce back past its rest shape once. Unscaled time, so it works in the pause menu, and
    // nothing runs while the spring is at rest.
    public class UiPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float pressedScale = 0.93f;
        public float stretch = 1.03f;
        public float stiffness = 520f;
        public float damping = 20f;
        // Set by UiStyle for buttons with a lip: it slides under the body while pressed.
        public RectTransform lip;
        public float lipDepth;

        float _target, _x, _v;

        public void OnPointerDown(PointerEventData eventData) => _target = 1f;

        public void OnPointerUp(PointerEventData eventData) => _target = 0f;

        public void OnPointerExit(PointerEventData eventData) => _target = 0f;

        void OnDisable()
        {
            _target = _x = _v = 0f;
            Apply();
        }

        void Update()
        {
            if (_x == _target && _v == 0f) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            _v += (-stiffness * (_x - _target) - damping * _v) * dt;
            _x += _v * dt;
            if (Mathf.Abs(_x - _target) < 0.002f && Mathf.Abs(_v) < 0.02f)
            {
                _x = _target;
                _v = 0f;
            }
            Apply();
        }

        void Apply()
        {
            transform.localScale = new Vector3(Mathf.LerpUnclamped(1f, stretch, _x), Mathf.LerpUnclamped(1f, pressedScale, _x), 1f);
            if (lip == null) return;
            float d = lipDepth * (1f - 0.75f * Mathf.Clamp01(_x));
            lip.offsetMin = new Vector2(0f, -d);
            lip.offsetMax = new Vector2(0f, -d);
        }
    }
}
