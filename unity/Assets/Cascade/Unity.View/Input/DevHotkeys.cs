#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
// Dev builds only (TRD 12.3, 13.8): opens the dev settings page with F1 on desktop (F2 toggles auto-play, F3 its plan-ahead mode) and a three-finger tap on a phone.
using Cascade.Presentation.Shell;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Recognizes a three-finger tap from the number of fingers down over time: three fingers (or more) were down together and all were
    /// lifted again within <see cref="MaxSeconds"/>. Pure, so it can be tested without a device.
    /// </summary>
    public sealed class ThreeFingerTapDetector
    {
        public int Fingers { get; set; } = 3;

        /// <summary>The longest press that still counts as a tap.</summary>
        public double MaxSeconds { get; set; } = 0.6;

        bool active;
        double start;
        int peak;

        /// <summary>Feed the number of fingers down every frame; true on the frame the last finger of a tap is lifted.</summary>
        public bool Feed(int fingersDown, double timeSeconds)
        {
            if (fingersDown > 0)
            {
                if (!active) { active = true; start = timeSeconds; peak = 0; }
                if (fingersDown > peak) peak = fingersDown;
                return false;
            }
            if (!active) return false;
            active = false;
            return peak >= Fingers && timeSeconds - start <= MaxSeconds;
        }
    }

    /// <summary>Toggles the dev page through <see cref="ScreenFlow.ToggleDev"/> on F1 or a three-finger tap (Input System EnhancedTouch).</summary>
    public sealed class DevHotkeys : MonoBehaviour
    {
        readonly ThreeFingerTapDetector tap = new ThreeFingerTapDetector();
        ScreenFlow? flow;
        DevViewModel? dev;

        [SerializeField, Tooltip("The longest three-finger press that still counts as a tap.")] float maxTapSeconds = 0.6f;

        public void Bind(ScreenFlow screenFlow, DevViewModel? devViewModel = null)
        {
            flow = screenFlow;
            dev = devViewModel;
        }

        void OnEnable() => EnsureTouch();

        void OnDisable()
        {
            if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable();
        }

        // The support can be switched off behind our back (the Input System is reset by tests, or by another user of it): ask again.
        static void EnsureTouch()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        void Update()
        {
            if (flow == null || !flow.DevAvailable) return;
            EnsureTouch();
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) flow.ToggleDev();
            if (kb != null && dev != null && kb.f2Key.wasPressedThisFrame) dev.ToggleAutoPlay();
            if (kb != null && dev != null && kb.f3Key.wasPressedThisFrame) dev.ToggleAutoPlayPlans();
            tap.MaxSeconds = maxTapSeconds;
            if (tap.Feed(Touch.activeTouches.Count, Time.unscaledTimeAsDouble)) flow.ToggleDev();
        }
    }
}
#endif
