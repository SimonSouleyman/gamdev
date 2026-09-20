using UnityEngine;
using UnityEngine.UI;

namespace Drift.UI
{
    // Pill switch built by UiStyle.Toggle: the knob slides and the mint tint fades on unscaled time, so it also
    // moves in the pause menu. Nothing runs once the knob has arrived; in Edit Mode Set snaps.
    public class UiToggle : MonoBehaviour
    {
        public float speed = 16f;

        RectTransform _knob;
        Image _tint;
        float _offX, _onX, _t, _target;

        public Text Label { get; private set; }
        public bool IsOn => _target > 0.5f;

        internal void Bind(Text label, RectTransform knob, Image tint, float offX, float onX)
        {
            Label = label;
            _knob = knob;
            _tint = tint;
            _offX = offX;
            _onX = onX;
        }

        public void Set(bool on, bool animate = true)
        {
            _target = on ? 1f : 0f;
            if (animate && Application.isPlaying && isActiveAndEnabled) return;
            _t = _target;
            Apply();
        }

        void OnDisable()
        {
            _t = _target;
            Apply();
        }

        void Update()
        {
            if (_t == _target) return;
            _t = Mathf.Lerp(_t, _target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            if (Mathf.Abs(_t - _target) < 0.004f) _t = _target;
            Apply();
        }

        void Apply()
        {
            if (_knob == null) return;
            _knob.anchoredPosition = new Vector2(Mathf.LerpUnclamped(_offX, _onX, _t), 0f);
            _tint.canvasRenderer.SetAlpha(_t);
        }
    }
}
