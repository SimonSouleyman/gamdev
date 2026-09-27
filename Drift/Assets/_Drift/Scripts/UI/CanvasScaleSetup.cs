using UnityEngine;
using UnityEngine.UI;

namespace Drift.UI
{
    [RequireComponent(typeof(CanvasScaler))]
    [ExecuteAlways]
    public class CanvasScaleSetup : MonoBehaviour
    {
        public Vector2 referenceResolution = new Vector2(1080f, 1920f);

        void OnEnable()
        {
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            // Expand keeps the whole reference layout visible on any aspect (phone portrait, landscape, tablet).
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }
    }
}
