using System;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.UI
{
    // A settings row in the house style: caption on the left, the current value on the right, a thumb-sized
    // slider underneath. Built like everything else in UiStyle - inset track, glossy mint fill, cream knob.
    public static class UiSlider
    {
        public const float RowHeight = 168f;
        const float TrackHeight = 26f;
        const float KnobSize = 76f;

        public static Slider Row(Transform parent, string name, string caption, Vector2 size, float value,
            Func<float, string> format, Action<float> onChanged, out Text valueLabel)
        {
            var root = UiStyle.Rect(parent, name);
            root.sizeDelta = size;

            var cap = UiStyle.Label(root, caption, UiStyle.Body, UiStyle.CreamSoft, TextAnchor.UpperLeft);
            cap.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(size.x * 0.66f, 48f));
            UiStyle.FitWidth(cap);
            valueLabel = UiStyle.Label(root, format != null ? format(value) : value.ToString("0.00"), UiStyle.Body, UiStyle.Sand, TextAnchor.UpperRight, true);
            valueLabel.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(size.x * 0.33f, 48f));

            // The whole lower half takes the touch, not just the thin track: a thumb never hits a 26-unit bar.
            var lane = UiStyle.Rect(root, "Lane").Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(size.x, KnobSize + 24f));
            var hit = UiStyle.Shape(lane, "Hit", UiSprites.PillOf(KnobSize), UiStyle.WithAlpha(Color.white, 0f), true);
            hit.rectTransform.Stretch();

            float inset = KnobSize * 0.5f;
            var track = UiStyle.Shape(lane, "Track", UiSprites.PillOf(TrackHeight), UiStyle.Track);
            track.rectTransform.Center(Vector2.zero, new Vector2(size.x, TrackHeight));

            // The fill ends under the middle of the knob, so the two never drift apart at full deflection.
            var fillArea = UiStyle.Rect(lane, "FillArea").Center(new Vector2(-inset * 0.5f, 0f), new Vector2(size.x - inset, TrackHeight));
            var fill = UiStyle.Shape(fillArea, "Fill", UiSprites.PillOf(TrackHeight), UiStyle.Mint);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            UiStyle.Shape(fill.transform, "Gloss", UiSprites.PillHighlightOf(TrackHeight), UiStyle.WithAlpha(Color.white, 0.4f)).rectTransform.Stretch(2f, 2f, 2f, 1f);

            var handleArea = UiStyle.Rect(lane, "HandleArea").Center(Vector2.zero, new Vector2(size.x - 2f * inset, KnobSize));
            var knob = UiStyle.Rect(handleArea, "Handle");
            knob.anchorMin = knob.anchorMax = new Vector2(0f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            // Height 0: the Slider drives the anchors to stretch the knob over its container, and a size on top
            // of that would make it twice as tall as the lane.
            knob.sizeDelta = new Vector2(KnobSize, 0f);
            knob.anchoredPosition = Vector2.zero;
            UiStyle.Shape(knob, "Shadow", UiSprites.SoftCircle, UiStyle.WithAlpha(UiStyle.Shadow, UiStyle.Shadow.a * 0.9f))
                .rectTransform.Center(new Vector2(0f, -KnobSize * 0.06f), new Vector2(KnobSize, KnobSize) * 1.25f);
            UiStyle.Shape(knob, "Lip", UiSprites.Circle, UiStyle.CreamDeep).rectTransform.Stretch(0f, -5f, 0f, 5f);
            UiStyle.Shape(knob, "Body", UiSprites.Circle, UiStyle.Cream).rectTransform.Stretch();
            UiStyle.Shape(knob, "Glint", UiSprites.SoftCircle, UiStyle.WithAlpha(Color.white, 0.9f))
                .rectTransform.Center(new Vector2(-KnobSize * 0.12f, KnobSize * 0.16f), new Vector2(KnobSize * 0.42f, KnobSize * 0.3f));

            var slider = lane.gameObject.AddComponent<Slider>();
            slider.targetGraphic = hit;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.transition = Selectable.Transition.None;
            slider.SetValueWithoutNotify(Mathf.Clamp01(value));

            var label = valueLabel;
            slider.onValueChanged.AddListener(v =>
            {
                if (label != null && format != null) label.text = format(v);
                onChanged?.Invoke(v);
            });
            return slider;
        }
    }
}
