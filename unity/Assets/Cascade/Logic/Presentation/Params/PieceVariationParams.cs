using Cascade.Config;

namespace Cascade.Presentation
{
    /// <summary>The <c>[pieces]</c> dials that <see cref="PieceVariation"/> reads.</summary>
    public sealed record PieceVariationParams(double YawDeg = 13, double SquashMin = 0.82, double Brightness = 0.06)
    {
        public static readonly PieceVariationParams Default = new PieceVariationParams();

        public static PieceVariationParams From(DialRegistry dials) =>
            new PieceVariationParams(dials.Pieces.VariationYawDeg, dials.Pieces.VariationSquashMin, dials.Pieces.VariationBrightness);
    }
}
