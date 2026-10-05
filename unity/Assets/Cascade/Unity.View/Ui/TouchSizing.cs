using Cascade.Config;
using Cascade.Platform.Abstractions;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Gives every button a physical minimum size on touch devices (PRD 5.3, TRD 12.2): <c>input.min_touch_target_mm</c> converted with
    /// <see cref="HostInfo.ScreenDpi"/> to screen pixels, then to panel units (the panel scales with the screen), set as the buttons'
    /// <c>min-width</c> / <c>min-height</c>. On a desktop with a mouse it does nothing. Screens that create buttons later (the map nodes,
    /// the dev page) call <see cref="Apply"/> for them. Pure arithmetic apart from the element writes: see <see cref="ToPanelUnits"/>.
    /// </summary>
    public sealed class TouchSizing
    {
        readonly DialRegistry dials;
        readonly HostInfo host;
        float minPanelUnits;

        public TouchSizing(DialRegistry dials, HostInfo host)
        {
            this.dials = dials;
            this.host = host;
        }

        /// <summary>True on a touch device: the minimum size applies.</summary>
        public bool Enabled => host.HasTouch || host.Kind == PlatformKind.Android;

        /// <summary>The minimum side in panel units (0 when not <see cref="Enabled"/> or before <see cref="Update"/>).</summary>
        public float MinPanelUnits => Enabled ? minPanelUnits : 0f;

        /// <summary>Millimetres to screen pixels at <paramref name="dpi"/> (a missing dpi counts as 160, Android's baseline).</summary>
        public static float Pixels(double mm, float dpi) => (float)(mm / 25.4 * (dpi > 1f ? dpi : 160f));

        /// <summary>Panel units per screen pixel is <c>panelWidth / screenWidth</c>.</summary>
        public static float ToPanelUnits(double mm, float dpi, float panelWidth, float screenWidth) =>
            screenWidth > 0f && panelWidth > 0f ? Pixels(mm, dpi) * panelWidth / screenWidth : 0f;

        /// <summary>Recomputes the minimum for the current panel scale and applies it to every button below <paramref name="root"/>.</summary>
        public void Update(VisualElement root, float panelWidth, float screenWidth)
        {
            minPanelUnits = ToPanelUnits(dials.Input.MinTouchTargetMm, host.ScreenDpi, panelWidth, screenWidth);
            Apply(root);
        }

        /// <summary>Applies the current minimum to the buttons at and below <paramref name="root"/> (new content).</summary>
        public void Apply(VisualElement root)
        {
            if (!Enabled || !(minPanelUnits > 0f)) return;
            root.Query<Button>().ForEach(b => Size(b));
            if (root is Button self) Size(self);
        }

        void Size(Button b)
        {
            b.style.minHeight = minPanelUnits;
            b.style.minWidth = minPanelUnits;
        }
    }
}
