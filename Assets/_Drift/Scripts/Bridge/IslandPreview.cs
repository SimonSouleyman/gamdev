using Drift.Islands;
using UnityEngine;

namespace Drift.Bridge
{
    // A second camera that frames the player island top-down into a small RenderTexture for the
    // session screens. It is a plain Camera without UniversalAdditionalCameraData, which URP treats
    // as a Base camera with post-processing off and no stacking with the main camera. The camera
    // component is only enabled on the frames that should render (throttled), so a hidden preview
    // costs nothing; in Edit Mode it stays enabled while an editorPreview screen is shown.
    public sealed class IslandPreview
    {
        public const string CameraName = "IslandPreviewCamera";

        public int size = 512;
        public float pitch = 62f;
        public float fieldOfView = 38f;
        public float margin = 1.18f;
        public float minRadius = 4f;
        public Color background = new Color(0.08f, 0.26f, 0.44f, 1f);

        Camera _cam;
        RenderTexture _rt;
        Transform _parent;
        float _timer;
        bool _pending = true;

        public Texture Texture => _rt;
        public Camera Camera => _cam;
        public bool IsRendering => _cam != null && _cam.enabled;

        public IslandPreview(Transform parent)
        {
            _parent = parent;
        }

        public static void DestroyExisting(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (child.name != CameraName) continue;
                if (Application.isPlaying) Object.Destroy(child);
                else Object.DestroyImmediate(child);
            }
        }

        public void RequestRender() => _pending = true;

        public void Ensure()
        {
            if (_rt == null || _rt.width != size)
            {
                Release();
                _rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                {
                    name = "IslandPreviewRT",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                    useMipMap = false,
                };
                _rt.Create();
                _pending = true;
            }
            else if (!_rt.IsCreated())
            {
                _rt.Create();
                _pending = true;
            }

            if (_cam == null)
            {
                var go = new GameObject(CameraName, typeof(Camera));
                go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                go.transform.SetParent(_parent, false);
                _cam = go.GetComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = background;
                _cam.fieldOfView = fieldOfView;
                _cam.nearClipPlane = 0.5f;
                _cam.farClipPlane = 400f;
                _cam.depth = -100f;
                _cam.allowHDR = false;
                _cam.allowMSAA = false;
                _cam.useOcclusionCulling = false;
                _cam.cullingMask = ~0;
            }
            if (_cam.targetTexture != _rt) _cam.targetTexture = _rt;
        }

        public void Frame(Island target)
        {
            if (_cam == null || target == null) return;
            float r = Mathf.Max(minRadius, target.BoundingRadius) * margin;
            float dist = r / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 dir = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
            Vector3 center = new Vector3(target.PlanarPosition.x, 0f, target.PlanarPosition.y);
            var t = _cam.transform;
            t.position = center - dir * dist;
            t.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _cam.fieldOfView = fieldOfView;
            _cam.farClipPlane = Mathf.Max(60f, dist * 3f);
        }

        // fps <= 0 renders once (on RequestRender / first show), otherwise at most fps times per second.
        public void Tick(Island target, bool visible, float fps, float unscaledDt)
        {
            if (!visible || target == null)
            {
                SetEnabled(false);
                _pending = true;
                return;
            }
            Ensure();
            if (!Application.isPlaying)
            {
                Frame(target);
                SetEnabled(true);
                return;
            }
            if (_cam.enabled) SetEnabled(false);
            _timer -= unscaledDt;
            if (!_pending && (fps <= 0f || _timer > 0f)) return;
            _pending = false;
            _timer = fps > 0f ? 1f / fps : float.MaxValue;
            Frame(target);
            SetEnabled(true);
        }

        // Immediate render for verification and for a frozen picture; the throttled path is Tick().
        public void RenderNow(Island target)
        {
            Ensure();
            Frame(target);
            _cam.Render();
        }

        void SetEnabled(bool on)
        {
            if (_cam != null && _cam.enabled != on) _cam.enabled = on;
        }

        public void Release()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                if (Application.isPlaying) Object.Destroy(_rt);
                else Object.DestroyImmediate(_rt);
                _rt = null;
            }
        }

        public void Dispose()
        {
            Release();
            if (_cam != null)
            {
                var go = _cam.gameObject;
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
                _cam = null;
            }
        }
    }
}
