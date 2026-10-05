using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>The per-instance look of one piece (all derived from its tile id; see <see cref="PieceVariation.For"/>).</summary>
    public readonly struct PieceLook
    {
        public PieceLook(double yawDegrees, double squash, bool squashOnX, double brightness, int fleckVariant, double fleckOffsetX, double fleckOffsetY)
        {
            YawDegrees = yawDegrees; Squash = squash; SquashOnX = squashOnX; Brightness = brightness;
            FleckVariant = fleckVariant; FleckOffsetX = fleckOffsetX; FleckOffsetY = fleckOffsetY;
        }

        /// <summary>Rotation of the body about the vertical axis, degrees, within plus or minus <c>variation_yaw_deg</c>.</summary>
        public double YawDegrees { get; }
        /// <summary>Scale of ONE horizontal axis, within <c>variation_squash_min..1</c> (the other axis stays 1).</summary>
        public double Squash { get; }
        /// <summary>True: the squash applies to the piece's X axis; false: to its Y (depth) axis.</summary>
        public bool SquashOnX { get; }
        /// <summary>Color multiplier around 1, within plus or minus <c>variation_brightness</c>.</summary>
        public double Brightness { get; }
        /// <summary>Which of <see cref="PieceVariation.FleckVariants"/> fleck textures to use (0-based).</summary>
        public int FleckVariant { get; }
        /// <summary>UV offset of the fleck texture, 0..1 each (the texture wraps).</summary>
        public double FleckOffsetX { get; }
        public double FleckOffsetY { get; }
    }

    /// <summary>
    /// Per-instance variation of pieces (yaw, squash, brightness, fleck positions), a pure function of the tile
    /// id and the <c>[pieces]</c> dials (TRD 12.1). Same id and params give the same look on every platform: it uses
    /// the PCG32 generator of Core exactly, and never <c>GetHashCode</c>.
    /// </summary>
    public static class PieceVariation
    {
        /// <summary>Number of fleck texture variants the look may pick from.</summary>
        public const int FleckVariants = 8;

        /// <summary>
        /// The draw order, so a tile keeps its yaw, squash, brightness, flip and fleck variant:
        /// yaw, squash, brightness, flip, fleck variant; the two fleck offsets are drawn after them.
        /// </summary>
        public static PieceLook For(int tileId, PieceVariationParams p)
        {
            ulong seed = unchecked((ulong)(long)tileId * 2654435761UL + 12345UL);
            var rng = new Pcg32(seed, 54UL);
            double yaw = (rng.Random() * 2 - 1) * p.YawDeg;
            double squash = p.SquashMin + rng.Random() * (1.0 - p.SquashMin);
            double bright = 1.0 + (rng.Random() * 2 - 1) * p.Brightness;
            bool onX = rng.RandBelow(2) == 1;
            int fleck = rng.RandBelow(FleckVariants);
            double ox = rng.Random();
            double oy = rng.Random();
            return new PieceLook(yaw, squash, onX, bright, fleck, ox, oy);
        }
    }
}
