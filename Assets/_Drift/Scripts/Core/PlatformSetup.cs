using UnityEngine;

namespace Drift.Core
{
    // Startup settings that depend on the device rather than on a scene. Application.targetFrameRate left at -1
    // means "platform default", and on Android and iOS that default is 30 fps: without this the game could never
    // run at 60 on a phone, however cheap a frame is. Desktop and the Editor keep their own pacing.
    public static class PlatformSetup
    {
        public const int MobileFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (Application.isMobilePlatform) Application.targetFrameRate = MobileFrameRate;
        }
    }
}
