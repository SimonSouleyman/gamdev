using UnityEngine;

namespace Drift.Islands
{
    public class IslandChaseCamera : MonoBehaviour
    {
        public Island target;
        public float distanceBehind = 7f;
        public float height = 9f;
        public float followLerp = 5f;
        public float lookLerp = 7f;
        public float referenceRadius = 3f;
        public float zoomExponent = 0.85f;
        public float shakeAmount = 0.9f;
        public float shakeDecay = 1.6f;

        float _shake;

        void OnEnable()
        {
            Island.Impact += OnImpact;
        }

        void OnDisable()
        {
            Island.Impact -= OnImpact;
        }

        void OnImpact(float intensity)
        {
            _shake = Mathf.Max(_shake, intensity);
        }

        void LateUpdate()
        {
            if (target == null) return;

            float scale = Mathf.Pow(Mathf.Max(1f, target.BoundingRadius / referenceRadius), zoomExponent);
            Vector3 up = target.Normal;
            Vector3 back = -target.Forward;
            Vector3 desiredPos = target.transform.position + up * (height * scale) + back * (distanceBehind * scale);
            Quaternion desiredRot = Quaternion.LookRotation((target.transform.position - desiredPos).normalized, up);

            float posT = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            float rotT = 1f - Mathf.Exp(-lookLerp * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPos, posT);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotT);

            if (_shake > 0.001f)
            {
                float t = Time.time * 28f;
                Vector3 offset = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f);
                transform.position += offset * (_shake * shakeAmount * scale);
                _shake *= Mathf.Exp(-shakeDecay * Time.deltaTime);
            }
        }
    }
}
