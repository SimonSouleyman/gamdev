using UnityEngine;

namespace Drift.UI
{
    // Every activation fades the screen in while its panel rises a little and pops from 94 % to full size with a
    // tiny overshoot. Unscaled time, so it also plays while the game is paused; in Edit Mode nothing runs and the
    // screen simply rests at full alpha.
    [RequireComponent(typeof(CanvasGroup))]
    public class UiFadeIn : MonoBehaviour
    {
        public RectTransform slide;
        public float duration = 0.26f;
        public float rise = 20f;
        public float pop = 0.06f;

        CanvasGroup _group;
        Vector2 _rest;
        bool _hasRest;
        float _t = 1f;

        void OnEnable()
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (slide != null && !_hasRest)
            {
                _rest = slide.anchoredPosition;
                _hasRest = true;
            }
            _t = 0f;
            Apply();
        }

        void OnDisable()
        {
            _t = 1f;
            Apply();
        }

        void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
            Apply();
        }

        void Apply()
        {
            float e = 1f - (1f - _t) * (1f - _t);
            if (_group != null) _group.alpha = Mathf.Clamp01(e * 1.6f);
            if (slide == null || !_hasRest) return;
            slide.anchoredPosition = _rest + new Vector2(0f, -rise * (1f - e));
            // Back-ease: passes 1 at about 70 % of the way and settles from just above it.
            float u = _t - 1f;
            float back = 1f + u * u * (2.7f * u + 1.7f);
            float s = 1f - pop * (1f - back);
            slide.localScale = new Vector3(s, s, 1f);
        }
    }
}
