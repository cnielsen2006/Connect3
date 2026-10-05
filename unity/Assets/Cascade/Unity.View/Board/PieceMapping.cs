using Cascade.Core;
using PresentationLook = Cascade.Presentation.PieceLook;

namespace Cascade.Unity.View
{
    /// <summary>Maps the logic's values to what the style draws (no decisions, just names).</summary>
    public static class PieceMapping
    {
        /// <summary>Core <see cref="Special"/> to the overlay of the piece. A Line on axis 0 (a row) is a horizontal vein.</summary>
        public static PieceSpecial ToView(Special? special)
        {
            switch (special)
            {
                case Special.Line line: return line.Axis == 0 ? PieceSpecial.LineHorizontal : PieceSpecial.LineVertical;
                case Special.Bomb _: return PieceSpecial.Bomb;
                case Special.Color _: return PieceSpecial.Color;
                default: return PieceSpecial.None;
            }
        }

        /// <summary><see cref="Cascade.Presentation.PieceVariation"/>'s result as the style's per-instance look.</summary>
        public static PieceLook ToView(PresentationLook l) => new PieceLook(
            (float)l.YawDegrees,
            (float)l.Squash,
            (float)l.Brightness,
            (float)(l.FleckOffsetX * 2.0 - 1.0),
            (float)(l.FleckOffsetY * 2.0 - 1.0),
            squashAlongZ: !l.SquashOnX);
    }
}
