using UnityEngine;
using UnityEngine.InputSystem;

namespace Drift.UI
{
    // A PC with a touch panel reports Touchscreen.current too, so "has a touchscreen" is not "plays by touch".
    // The mode follows the device that was used last; phones start in touch mode.
    public static class InputMode
    {
        static bool _touch;
        static bool _init;
        static int _frame = -1;

        public static bool TouchPreferred
        {
            get
            {
                Poll();
                return _touch;
            }
        }

        public static void Poll()
        {
            if (!_init)
            {
                _init = true;
                _touch = Application.isMobilePlatform;
            }
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;

            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) { _touch = true; return; }
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) { _touch = false; return; }
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)) _touch = false;
        }
    }
}
