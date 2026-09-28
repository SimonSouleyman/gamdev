using System;
using UnityEngine;

namespace Drift.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class SynthAudioOutput : MonoBehaviour
    {
        public float Gain = 1f;
        public ISynthSource Source { get; private set; }
        public bool ClipMode { get; private set; }
        public int FilterCalls => _filterCalls;
        public int ClipReads => _clipReads;

        volatile int _filterCalls;
        volatile int _clipReads;
        int _sampleRate = 48000;
        AudioSource _audio;
        AudioClip _clip;

        public void Begin(ISynthSource source, bool clipStreaming)
        {
            Source = source;
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.loop = true;
            _audio.volume = 1f;
            _audio.priority = 0;
            if (clipStreaming) SwitchToClipStreaming();
            else
            {
                ClipMode = false;
                _audio.clip = null;
                _audio.Play();
            }
        }

        public void SwitchToClipStreaming()
        {
            if (_audio == null) _audio = GetComponent<AudioSource>();
            ClipMode = true;
            _audio.Stop();
            if (_clip == null)
                _clip = AudioClip.Create(name + "Stream", _sampleRate, 2, _sampleRate, true, OnPcmRead, OnPcmSetPosition);
            _audio.clip = _clip;
            _audio.loop = true;
            _audio.Play();
        }

        void OnDisable()
        {
            if (_audio != null) _audio.Stop();
        }

        void OnDestroy()
        {
            if (_clip != null)
            {
                if (Application.isPlaying) Destroy(_clip); else DestroyImmediate(_clip);
                _clip = null;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (ClipMode) return;
            _filterCalls++;
            Fill(data, channels);
        }

        void OnPcmRead(float[] data)
        {
            _clipReads++;
            Fill(data, 2);
        }

        void OnPcmSetPosition(int position) { }

        void Fill(float[] data, int channels)
        {
            var src = Source;
            float g = Gain;
            if (src == null || g <= 0f)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            src.Render(data, channels, _sampleRate);
            if (g != 1f)
                for (int i = 0; i < data.Length; i++) data[i] *= g;
        }
    }
}
