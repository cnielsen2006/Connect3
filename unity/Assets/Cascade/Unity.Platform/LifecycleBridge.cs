using System;
using Cascade.Platform.Abstractions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cascade.Unity.Platform
{
    /// <summary>
    /// The Unity side of <see cref="IAppLifecycle"/> (TRD 13.6). It has no logic of its own, it only turns Unity's callbacks and
    /// device state into the four events the game knows:
    /// <list type="bullet">
    /// <item><see cref="ScreenChanged"/>: Unity has no resize event on mobile, so size, orientation and safe area are polled
    /// every frame and the event is raised when any of them changes (and once at start-up, see <see cref="Publish"/>).</item>
    /// <item><see cref="Paused"/> / <see cref="Resumed"/>: from <c>OnApplicationPause</c> and <c>OnApplicationFocus</c>. It never
    /// relies on <c>OnApplicationQuit</c> (Android does not call it reliably): the owner saves on <see cref="Paused"/>.</item>
    /// <item><see cref="BackPressed"/>: the Escape key through the Input System. The Input System reports the Android Back key
    /// as Escape, so one binding serves desktop and phone.</item>
    /// </list>
    /// The screen can be overridden (<see cref="Override"/>) so tests and tools can render at any size or with a notch.
    /// </summary>
    public sealed class LifecycleBridge : MonoBehaviour, IAppLifecycle
    {
        ScreenInfo current;
        bool haveCurrent;
        ScreenInfo? overridden;
        bool appPaused;
        bool focused = true;
        bool paused;

        public event Action? Paused;
        public event Action? Resumed;
        public event Action? BackPressed;
        public event Action<ScreenInfo>? ScreenChanged;

        /// <summary>The screen as last published (the real one, or the override).</summary>
        public ScreenInfo Current => haveCurrent ? current : Read();

        /// <summary>True between a pause (background, focus lost) and the matching resume.</summary>
        public bool IsPaused => paused;

        /// <summary>Raises <see cref="ScreenChanged"/> with the current screen even if it did not change (the owner calls it once after it has subscribed).</summary>
        public void Publish()
        {
            current = Read();
            haveCurrent = true;
            ScreenChanged?.Invoke(current);
        }

        /// <summary>Pretends the screen has this size, safe area included; null goes back to the real screen. Takes effect now.</summary>
        public void Override(ScreenInfo? info)
        {
            overridden = info;
            Poll();
        }

        void Update()
        {
            Poll();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) BackPressed?.Invoke();
        }

        void Poll()
        {
            var now = Read();
            if (haveCurrent && Same(now, current)) return;
            current = now;
            haveCurrent = true;
            ScreenChanged?.Invoke(now);
        }

        ScreenInfo Read()
        {
            if (overridden.HasValue) return overridden.Value;
            int w = Screen.width, h = Screen.height;
            var sa = Screen.safeArea; // pixels, origin bottom left
            return new ScreenInfo(w, h, new SafeArea(
                Mathf.Max(0f, sa.xMin),
                Mathf.Max(0f, h - sa.yMax),
                Mathf.Max(0f, w - sa.xMax),
                Mathf.Max(0f, sa.yMin)));
        }

        static bool Same(ScreenInfo a, ScreenInfo b) =>
            a.Width == b.Width && a.Height == b.Height
            && a.SafeArea.Left == b.SafeArea.Left && a.SafeArea.Top == b.SafeArea.Top
            && a.SafeArea.Right == b.SafeArea.Right && a.SafeArea.Bottom == b.SafeArea.Bottom;

        void OnApplicationPause(bool pauseStatus)
        {
            appPaused = pauseStatus;
            UpdatePaused();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            UpdatePaused();
        }

        void UpdatePaused()
        {
            bool now = appPaused || !focused;
            if (now == paused) return;
            paused = now;
            if (now) Paused?.Invoke(); else Resumed?.Invoke();
        }

        /// <summary>For tests: behaves as if the app went to the background (true) or came back (false).</summary>
        public void SimulatePause(bool pause) => OnApplicationPause(pause);

        /// <summary>For tests: behaves as if the Back key was pressed.</summary>
        public void SimulateBack() => BackPressed?.Invoke();
    }
}
