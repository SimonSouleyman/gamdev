using UnityEngine;

namespace Drift.Core
{
    // Startup settings that depend on the device rather than on a scene. Application.targetFrameRate left at -1
    // means "platform default", and on Android and iOS that default is 30 fps: without this the game could never
    // run at 60 on a phone, however cheap a frame is. Desktop and the Editor keep their own pacing.
    // The screen must not dim while the app is open (owner, v0.6.3: "mein Handybildschirm geht beim Spielen aus"):
    // tilt steering and just watching never touch the screen, so the system timeout ran out mid-game.
    public static class PlatformSetup
    {
        public const int MobileFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (!Application.isMobilePlatform) return;
            Application.targetFrameRate = MobileFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
