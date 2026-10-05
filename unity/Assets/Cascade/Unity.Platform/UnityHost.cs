using Cascade.Platform.Abstractions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cascade.Unity.Platform
{
    /// <summary>The one place that asks "where am I running?" (TRD §13.3).</summary>
    public static class UnityHost
    {
        public static HostInfo Detect()
        {
            bool android = Application.platform == RuntimePlatform.Android;
            return new HostInfo(
                android ? PlatformKind.Android : PlatformKind.Desktop,
                HasMouse: Mouse.current != null,
                HasKeyboard: Keyboard.current != null,
                HasTouch: Touchscreen.current != null,
                Dev: Application.isEditor || Debug.isDebugBuild,
                ScreenDpi: Screen.dpi);
        }

        /// <summary>Display refresh rate, for target_fps = 0 (TRD §13.9).</summary>
        public static int RefreshRateHz()
        {
            var r = Screen.currentResolution.refreshRateRatio;
            return r.value > 0 ? Mathf.RoundToInt((float)r.value) : 60;
        }

        /// <summary>target_fps = 0 means the display refresh rate. Unity on Android caps at 30 otherwise.</summary>
        public static void ApplyFrameRate(int targetFps)
        {
            if (Application.isMobilePlatform) QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps > 0 ? targetFps : RefreshRateHz();
        }
    }
}
