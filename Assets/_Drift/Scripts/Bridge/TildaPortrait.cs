using System.Collections.Generic;
using Drift.Core;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // One shared 3D portrait of Tilda for every screen that shows her. The rig (model + a plain Base camera, see
    // IslandPreview) lives 3000 units below the sea, three far-clip lengths away from the main camera, on the unnamed
    // layer 31, and the portrait camera sees nothing but that layer. It exists only while at least one TildaView is
    // enabled and renders at most MaxFps times per second by enabling its camera on those frames only, so a hidden
    // Tilda costs nothing. The pose comes from the view that asked most recently.
    [ExecuteAlways]
    [AddComponentMenu("")]
    public sealed class TildaPortrait : MonoBehaviour
    {
        public const string RigName = "TildaPortraitRig";
        // Small uses (bubble, Anleitung corner) render 384 px, the big presenter beside a menu 768 px.
        public const int Size = 384;
        public const int LargeSize = 768;
        public const float MaxFps = 20f;
        public const int Layer = 31;
        // 4x on a 384 px target costs next to nothing and keeps her outline and eyes smooth at bubble size.
        public const int Msaa = 4;
        const float PreviewTime = 0.62f;

        static readonly Vector3 Home = new Vector3(0f, -3000f, 0f);
        static readonly List<TildaView> s_views = new List<TildaView>();
        static TildaPortrait s_instance;
        static bool s_failed;

        RenderTexture _rt;
        Camera _cam;
        Material _material;
        TildaParts _parts;
        TildaAnimator _animator;
        float _timer, _lastTime = -1f;
        bool _built, _fullBody;

        public static bool Active => s_instance != null;
        public static Texture Texture => s_instance != null ? s_instance._rt : null;
        public static Camera Camera => s_instance != null ? s_instance._cam : null;
        public static TildaParts Parts => s_instance != null ? s_instance._parts : null;
        public static int Users => s_views.Count;
        // How far the current pose has lifted her off the ground (model units, 0.24 at the top of a cheer hop).
        public static float HopHeight => s_instance != null && s_instance._parts != null ? s_instance._parts.root.localPosition.y : 0f;
        public static int Resolution => s_instance != null && s_instance._rt != null ? s_instance._rt.width : 0;

        // Where her mouth is in the square picture (0..1 from the lower left corner), at rest: speech bubbles aim
        // their tail at it. Measured from the rig (TildaVoiceTests keeps them honest).
        public static readonly Vector2 MouthBust = new Vector2(0.5f, 0.252f), MouthFullBody = new Vector2(0.5f, 0.321f);

        public static Vector2 MouthIn(bool fullBody) => fullBody ? MouthFullBody : MouthBust;

        // What she wears: the "schnelle Brille" in Abenteuer, her reading glasses otherwise. The override is for
        // previews and verification renders (null: follow the game mode).
        public static TildaAccessory? AccessoryOverride;
        public static TildaAccessory WantedAccessory =>
            AccessoryOverride ?? (GameModes.IsAdventure ? TildaAccessory.SportShades : TildaAccessory.ReadingGlasses);

        void Dress()
        {
            var wanted = WantedAccessory;
            if (_parts.accessory != wanted) TildaModel.SetAccessory(_parts, wanted);
        }

        // The same, measured live from the rig's camera (null without a rig).
        public static Vector2? MeasureMouth()
        {
            if (s_instance == null || s_instance._parts == null || s_instance._cam == null) return null;
            Vector3 v = s_instance._cam.WorldToViewportPoint(s_instance._parts.mouth.position);
            return new Vector2(v.x, v.y);
        }

        // A RawImage showing the shared portrait; it acquires the rig while enabled. Place it like any other rect.
        public static RawImage CreateImage(RectTransform parent, Vector2 size)
        {
            var go = new GameObject("Tilda", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var raw = go.GetComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.white;
            raw.rectTransform.sizeDelta = size;
            go.AddComponent<TildaView>();
            return raw;
        }

        // ---------------------------------------------------------------- users

        internal static void Acquire(TildaView view)
        {
            s_views.RemoveAll(v => v == null || v == view);
            s_views.Add(view);
            if (s_instance != null) s_instance._timer = 0f;
            view.Bind(Texture);
        }

        // The rig is built on first use, not in Acquire: screens build their Tilda and hide it again in one go.
        internal static void Ensure()
        {
            if (s_instance != null || s_failed || s_views.Count == 0) return;
            s_instance = Create();
            s_failed = s_instance == null;
            foreach (var v in s_views) if (v != null) v.Bind(Texture);
        }

        internal static void Release(TildaView view)
        {
            s_views.RemoveAll(v => v == null || v == view);
            if (s_views.Count > 0 || s_instance == null) return;
            var rig = s_instance;
            s_instance = null;
            rig.Teardown();
        }

        internal static void Touch(TildaView view)
        {
            int i = s_views.IndexOf(view);
            if (i < 0) return;
            if (i != s_views.Count - 1)
            {
                s_views.RemoveAt(i);
                s_views.Add(view);
            }
            Ensure();
            if (s_instance == null) return;
            s_instance.FitResolution();
            if (Application.isPlaying) s_instance._timer = 0f;
            else s_instance.Render();
        }

        static int WantedSize()
        {
            int size = 0;
            foreach (var v in s_views) if (v != null && v.isActiveAndEnabled) size = Mathf.Max(size, v.Resolution);
            return size > 0 ? size : Size;
        }

        static TildaView Current
        {
            get
            {
                for (int i = s_views.Count - 1; i >= 0; i--)
                    if (s_views[i] != null && s_views[i].isActiveAndEnabled) return s_views[i];
                return null;
            }
        }

        // Immediate render of the current (or the given) pose as a still: Edit Mode previews and verification.
        public static void RenderNow()
        {
            Ensure();
            if (s_instance != null) s_instance.Render();
        }

        public static void RenderNow(TildaPose pose, bool talking, float time)
        {
            Ensure();
            if (s_instance == null) return;
            s_instance.FitResolution();
            var view = Current;
            s_instance._animator.VoiceLevel = -1f;
            s_instance._animator.PresentSide = view != null ? view.PresentSide : 1f;
            s_instance.Frame(view != null && view.FullBody);
            s_instance.Dress();
            s_instance._animator.Apply(pose, talking, time, 0f, true);
            s_instance._cam.Render();
        }

        // ---------------------------------------------------------------- rig

        static TildaPortrait Create()
        {
            var go = new GameObject(RigName) { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
            go.SetActive(false);
            go.transform.position = Home;
            var rig = go.AddComponent<TildaPortrait>();
            if (!rig.Build())
            {
                rig.Teardown();
                return null;
            }
            go.SetActive(true);
            return rig;
        }

        bool Build()
        {
            _material = TildaModel.CreateMaterial();
            if (_material == null) return false;
            _parts = TildaModel.Build(transform, _material, Layer);
            _animator = new TildaAnimator(_parts);

            _rt = CreateTarget(WantedSize());

            var camGo = new GameObject("TildaPortraitCamera", typeof(Camera)) { layer = Layer, hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
            camGo.transform.SetParent(transform, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.enabled = false;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(UiStyle.Glass.r, UiStyle.Glass.g, UiStyle.Glass.b, 0f);
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = 24f;
            _cam.nearClipPlane = 2f;
            _cam.farClipPlane = 20f;
            _cam.depth = -90f;
            _cam.allowHDR = false;
            _cam.allowMSAA = Msaa > 1;
            _cam.useOcclusionCulling = false;
            _cam.targetTexture = _rt;
            _cam.aspect = 1f;
            _fullBody = true;
            Frame(false);
            Dress();

            _animator.Apply(TildaPose.Idle, false, PreviewTime, 0f, true);
            _built = true;
            return true;
        }

        // Bust (bubble, corner): close, her base runs out of the lower edge. Full body (standing beside a menu): a
        // little wider and lower, so the whole base and the ground in front of it are in the picture.
        void Frame(bool fullBody)
        {
            if (fullBody == _fullBody) return;
            _fullBody = fullBody;
            _cam.fieldOfView = fullBody ? 26f : 21f;
            Vector3 target = new Vector3(0f, fullBody ? 1.3f : 1.38f, 0f);
            Vector3 dir = Quaternion.Euler(13f, 0f, 0f) * Vector3.forward;
            _cam.transform.localPosition = target - dir * 9.3f;
            _cam.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        static RenderTexture CreateTarget(int size)
        {
            // 24 bit: the layers of her eyes (iris, pupil, shine, lids) are a few millimetres apart.
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                name = "TildaPortraitRT",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = Msaa,
                useMipMap = false,
            };
            rt.Create();
            return rt;
        }

        // The target follows the largest enabled view; every view is rebound to the new texture.
        void FitResolution()
        {
            if (!_built) return;
            int wanted = WantedSize();
            if (_rt != null && _rt.width == wanted) return;
            var old = _rt;
            _rt = CreateTarget(wanted);
            _cam.targetTexture = _rt;
            foreach (var v in s_views) if (v != null) v.Bind(_rt);
            if (old != null)
            {
                old.Release();
                Kill(old);
            }
            _timer = 0f;
        }

        // Her mouth follows the grumble synth's real loudness. With the grumble switched off it runs on the talk
        // cycle, but only while a bubble is still typing: a remark stays up longer than she "says" it.
        void Pose(TildaView view, float time, float dt, bool snap)
        {
            bool voiced = TildaVoice.Grumbling;
            _animator.VoiceLevel = voiced ? TildaVoice.Level : -1f;
            _animator.PresentSide = view != null ? view.PresentSide : 1f;
            Frame(view != null && view.FullBody);
            Dress();
            bool talking = view != null && view.Talking && (!TildaVoice.Speaking || TildaBubble.TypingNow);
            _animator.Apply(view != null ? view.Pose : TildaPose.Idle, voiced || talking, time, dt, snap);
        }

        void OnEnable()
        {
#if UNITY_EDITOR
            // After a domain reload the statics and private fields are gone but the DontSave rig is still there.
            if (!_built) UnityEditor.EditorApplication.delayCall += RemoveOrphan;
#endif
        }

#if UNITY_EDITOR
        void RemoveOrphan()
        {
            if (this == null || _built) return;
            Teardown();
        }
#endif

        void Update()
        {
            if (!_built || !Application.isPlaying) return;
            if (_cam.enabled) _cam.enabled = false;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            var view = Current;
            if (view == null) return;
            _timer = 1f / MaxFps;
            float now = Time.unscaledTime;
            float dt = _lastTime < 0f ? 0f : Mathf.Min(0.25f, now - _lastTime);
            FitResolution();
            Pose(view, now, dt, _lastTime < 0f);
            _lastTime = now;
            _cam.enabled = true;
        }

        void Render()
        {
            if (!_built) return;
            FitResolution();
            Pose(Current, Application.isPlaying ? Time.unscaledTime : PreviewTime, 0f, true);
            _cam.Render();
        }

        // Everything is found through the hierarchy, so an orphan left by a domain reload cleans up just the same.
        void Teardown()
        {
            _built = false;
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                cam.enabled = false;
                var rt = cam.targetTexture;
                cam.targetTexture = null;
                if (rt != null)
                {
                    rt.Release();
                    Kill(rt);
                }
            }
            Material material = null;
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true)) if (r.sharedMaterial != null) material = r.sharedMaterial;
            foreach (var f in GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) Kill(f.sharedMesh);
            if (material != null) Kill(material);
            gameObject.SetActive(false);
            Kill(gameObject);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
