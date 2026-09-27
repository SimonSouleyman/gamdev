using UnityEngine;

namespace Drift.UI
{
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rt;
        Rect _lastSafe;
        Vector2Int _lastScreen;

        void OnEnable()
        {
            _rt = (RectTransform)transform;
            _lastScreen = Vector2Int.zero;
            Apply();
        }

        void Update()
        {
            if (Screen.safeArea != _lastSafe || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
                Apply();
        }

        void Apply()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return;
            Rect safe = Screen.safeArea;
            _lastSafe = safe;
            _lastScreen = new Vector2Int(w, h);
            _rt.anchorMin = new Vector2(safe.xMin / w, safe.yMin / h);
            _rt.anchorMax = new Vector2(safe.xMax / w, safe.yMax / h);
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
