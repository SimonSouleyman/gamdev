using UnityEngine;
using UnityEngine.InputSystem;

namespace Drift.UI
{
    public class MobileMoveInput : MonoBehaviour, IMoveInputSource
    {
        [Range(0f, 0.5f)] public float deadZone = 0.12f;
        public string stickBinding = "<Gamepad>/leftStick";

        InputAction _stick;

        public Vector2 Move { get; private set; }

        void OnEnable()
        {
            _stick = new InputAction("VirtualMove", InputActionType.Value, stickBinding);
            _stick.Enable();
        }

        void OnDisable()
        {
            if (_stick == null) return;
            _stick.Disable();
            _stick.Dispose();
            _stick = null;
            Move = Vector2.zero;
        }

        void Update()
        {
            if (_stick == null) return;
            Vector2 v = _stick.ReadValue<Vector2>();
            float mag = v.magnitude;
            if (mag < deadZone) { Move = Vector2.zero; return; }
            float scaled = Mathf.Min(1f, (mag - deadZone) / (1f - deadZone));
            Move = v / mag * scaled;
        }
    }
}
