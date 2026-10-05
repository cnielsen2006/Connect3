using Cascade.Config;

namespace Cascade.Presentation
{
    /// <summary>
    /// Pointer rules for <see cref="InputStateMachine"/>. <see cref="InnerFraction"/> is the central
    /// fraction of a neighbor cell that counts as "well inside" (PRD 2.2): it is
    /// <c>input.drag_inner_fraction</c> for a mouse and <c>input.touch_inner_fraction</c> for touch
    /// (<see cref="ForMouse"/> / <see cref="ForTouch"/>). <see cref="DragMinCos"/> is how well a drag must line up
    /// with a neighbor's direction (cosine of the angle) to target it; the swap target follows the drag vector, not the cell under the pointer.
    /// </summary>
    public sealed record InputParams(double InnerFraction = 0.6, double DoubleClickMs = 300, double DragMinCos = 0.8)
    {
        public static readonly InputParams Default = new InputParams();

        public static InputParams ForMouse(DialRegistry dials) =>
            new InputParams(dials.Input.DragInnerFraction, dials.Input.DoubleClickMs);

        public static InputParams ForTouch(DialRegistry dials) =>
            new InputParams(dials.Input.TouchInnerFraction, dials.Input.DoubleTapMs);

        public static InputParams From(DialRegistry dials, bool touch) => touch ? ForTouch(dials) : ForMouse(dials);
    }
}
