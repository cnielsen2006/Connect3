using Cascade.Config;

namespace Cascade.Presentation
{
    /// <summary>
    /// Thresholds of <see cref="GestureRecognizer"/>, in millimetres and milliseconds (TRD 13.5);
    /// the recognizer converts millimetres to pixels with the screen DPI.
    /// </summary>
    public sealed record GestureParams(
        double TapSlopMm = 2.0,
        double PinchMinMm = 4.0,
        double DoubleTapMs = 300,
        double DoubleTapDistanceMm = 8.0)
    {
        public static readonly GestureParams Default = new GestureParams();

        /// <summary>
        /// From the touch dials in <c>[input]</c>: <c>tap_slop_mm</c>, <c>pinch_min_mm</c>, <c>double_tap_ms</c>.
        /// <c>DoubleTapDistanceMm</c> is not a dial.
        /// </summary>
        public static GestureParams From(DialRegistry dials) => new GestureParams(
            dials.Input.TapSlopMm, dials.Input.PinchMinMm, dials.Input.DoubleTapMs);
    }
}
