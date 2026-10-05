using Cascade.Config;

namespace Cascade.Presentation
{
    /// <summary>Camera dials and constants of the board camera (TRD 12.2).</summary>
    public sealed record CameraParams
    {
        /// <summary>Fraction of the slot kept free around the board when auto-fitting (<c>camera.fit_margin</c>).</summary>
        public double FitMargin { get; init; } = 0.04;
        public double ZoomMin { get; init; } = 0.6;
        public double ZoomMax { get; init; } = 3.0;
        /// <summary>Zoom factor per wheel notch.</summary>
        public double ZoomStep { get; init; } = 1.12;
        /// <summary>Tilt away from straight down, in degrees (<c>camera.pitch_deg</c>).</summary>
        public double PitchDeg { get; init; } = 25;
        /// <summary>Vertical field of view in degrees (not a dial; the style picks it).</summary>
        public double FovDeg { get; init; } = 30;
        /// <summary>Height of the plane that picking uses: about the middle of a piece.</summary>
        public double PickHeight { get; init; } = 0.17;
        /// <summary>Tray lip width, tray depth and the height of the tallest thing on the board (a lifted piece):
        /// the fit includes the box these span so no part of the tray is cropped.</summary>
        public double TrayLip { get; init; } = 0.16;
        public double TrayDepth { get; init; } = 0.25;
        public double PieceTop { get; init; } = 0.45;

        public static readonly CameraParams Default = new CameraParams();

        public static CameraParams From(DialRegistry dials)
        {
            var c = dials.Camera;
            return new CameraParams
            {
                FitMargin = c.FitMargin,
                ZoomMin = c.ZoomMin,
                ZoomMax = c.ZoomMax,
                ZoomStep = c.ZoomStep,
                PitchDeg = c.PitchDeg,
            };
        }
    }
}
