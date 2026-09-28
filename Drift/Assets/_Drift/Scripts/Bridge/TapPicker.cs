using System.Collections.Generic;
using UnityEngine;

namespace Drift.Bridge
{
    public enum TapReject { None, NotPlaying, OverUi, MultiTouch, Pinching, Steered, HeldTooLong, MovedTooFar, NothingNear }

    // What is known about one press (mouse button or finger) when it is released.
    public struct TapPress
    {
        public bool overUi, multiTouch, pinching, steered;
        public float seconds, movedPixels;
    }

    // Screen-space nearest-candidate selection for taps: a candidate counts when it projects in front of the
    // camera (z > 0) and within radiusPx of the tap; the closest one wins. Pure so the tap logic is testable.
    public static class TapPicker
    {
        public static bool Consider(Vector2 tap, Vector3 screenPoint, float radiusPx, ref float best)
        {
            if (screenPoint.z <= 0f) return false;
            float dx = screenPoint.x - tap.x, dy = screenPoint.y - tap.y;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > radiusPx || d >= best) return false;
            best = d;
            return true;
        }

        public static int Nearest(Vector2 tap, IReadOnlyList<Vector3> screenPoints, float radiusPx, out float distance)
        {
            int index = -1;
            float best = float.MaxValue;
            for (int i = 0; i < screenPoints.Count; i++)
                if (Consider(tap, screenPoints[i], radiusPx, ref best)) index = i;
            distance = index >= 0 ? best : -1f;
            return index;
        }

        // Canvas units -> pixels. The canvas scaler (1080 x 1920, Expand) shrinks to 0.56 in a landscape window,
        // which would leave a 40-unit radius at 22 px; the shorter screen side against 1080 is the floor, so a
        // 64-unit radius is 64 px on any 1080p screen, portrait or landscape.
        public static float PixelsPerUnit(float canvasScale, float screenWidth, float screenHeight)
        {
            float shortSide = Mathf.Min(screenWidth, screenHeight) / 1080f;
            return Mathf.Max(0.25f, Mathf.Max(canvasScale, shortSide));
        }

        // Whether a released press is a tap on the world. The stick only vetoes a press it actually steered with,
        // and the mouse never has stick/multi/pinch set.
        public static TapReject Gate(bool playing, in TapPress press, float maxSeconds, float maxMovePixels)
        {
            if (!playing) return TapReject.NotPlaying;
            if (press.overUi) return TapReject.OverUi;
            if (press.multiTouch) return TapReject.MultiTouch;
            if (press.pinching) return TapReject.Pinching;
            if (press.steered) return TapReject.Steered;
            if (press.seconds > maxSeconds) return TapReject.HeldTooLong;
            if (press.movedPixels > maxMovePixels) return TapReject.MovedTooFar;
            return TapReject.None;
        }
    }
}
