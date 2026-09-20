using System;
using UnityEngine;

namespace Drift.UI
{
    // Layouts with a breakpoint (one line on wide screens, two on narrow ones) hang their switch on this instead
    // of polling the rect every frame.
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UiResizeWatcher : MonoBehaviour
    {
        public Action Resized;

        void OnRectTransformDimensionsChange() => Resized?.Invoke();
    }
}
