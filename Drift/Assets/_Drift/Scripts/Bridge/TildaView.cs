using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // Sits on the RawImage made by TildaPortrait.CreateImage: holds the shared portrait alive while it is enabled
    // and carries the pose this screen wants. Play() shows a pose for a moment and then falls back to the base pose.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    [AddComponentMenu("")]
    public sealed class TildaView : MonoBehaviour
    {
        RawImage _image;
        TildaPose _base = TildaPose.Idle, _temp, _then;
        bool _baseTalking;
        float _tempUntil = -1f, _talkUntil = -1f, _thenUntil = -1f;

        // Pixels of the shared portrait this view needs: the rig renders at the largest request of the enabled views.
        public int Resolution { get; private set; } = TildaPortrait.Size;
        // Present pose: +1 when what she points at is on the viewer's right, -1 on the left.
        public float PresentSide { get; set; } = 1f;
        // Whole figure with the ground in front of her (presenter) instead of the closer bust framing.
        public bool FullBody { get; private set; }

        public void SetFullBody(bool fullBody)
        {
            if (fullBody == FullBody) return;
            FullBody = fullBody;
            if (isActiveAndEnabled) TildaPortrait.Touch(this);
        }

        public void SetResolution(int pixels)
        {
            pixels = Mathf.Clamp(pixels, 64, TildaPortrait.LargeSize);
            if (pixels == Resolution) return;
            Resolution = pixels;
            if (isActiveAndEnabled) TildaPortrait.Touch(this);
        }

        public TildaPose Pose => !Application.isPlaying ? _base : Time.unscaledTime < _tempUntil ? _temp : Time.unscaledTime < _thenUntil ? _then : _base;
        public bool Talking => _baseTalking || (Application.isPlaying && Time.unscaledTime < _talkUntil);

        public void SetPose(TildaPose pose, bool talking = false)
        {
            bool changed = pose != _base || talking != _baseTalking || _tempUntil >= 0f || _talkUntil >= 0f || _thenUntil >= 0f;
            _base = pose;
            _baseTalking = talking;
            _tempUntil = _talkUntil = _thenUntil = -1f;
            if (changed || !Application.isPlaying) TildaPortrait.Touch(this);
        }

        // Shows pose for poseSeconds and moves the mouth for talkSeconds (both unscaled), then returns to the base
        // pose; with thenSeconds > poseSeconds the pose `then` fills the time in between (wave, then present).
        public void Play(TildaPose pose, float poseSeconds, float talkSeconds, TildaPose then = TildaPose.Idle, float thenSeconds = 0f)
        {
            _temp = pose;
            _then = then;
            _tempUntil = poseSeconds > 0f ? Time.unscaledTime + poseSeconds : -1f;
            _talkUntil = talkSeconds > 0f ? Time.unscaledTime + talkSeconds : -1f;
            _thenUntil = thenSeconds > 0f ? Time.unscaledTime + thenSeconds : -1f;
            TildaPortrait.Touch(this);
        }

        internal void Bind(Texture texture)
        {
            if (_image == null) _image = GetComponent<RawImage>();
            _image.texture = texture;
            _image.enabled = texture != null;
        }

        void OnEnable()
        {
            TildaPortrait.Acquire(this);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += TildaPortrait.RenderNow;
#endif
        }

        void OnDisable()
        {
            TildaPortrait.Release(this);
        }

        void Update()
        {
            if (_image != null && _image.texture == null) TildaPortrait.Ensure();
        }
    }
}
