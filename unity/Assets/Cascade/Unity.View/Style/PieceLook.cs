namespace Cascade.Unity.View
{
    /// <summary>
    /// Per-instance look of one piece (TRD 12.1). The later Presentation.PieceVariation function (pure of the
    /// tile id and the [pieces] dials) maps onto this struct.
    /// </summary>
    public readonly struct PieceLook
    {
        /// <summary>Rotation about the vertical axis, degrees.</summary>
        public readonly float YawDegrees;
        /// <summary>Scale of one horizontal axis, 0.82..1 (1 = none).</summary>
        public readonly float Squash;
        /// <summary>Colour multiplier around 1 (1 +- 0.06).</summary>
        public readonly float Brightness;
        /// <summary>Fleck shift, -1..1 of MaterialParams.fleckOffsetRange.</summary>
        public readonly float FleckOffsetX;
        public readonly float FleckOffsetY;
        /// <summary>True: squash the local Z axis instead of X (the per-tile flip).</summary>
        public readonly bool SquashAlongZ;

        public PieceLook(float yawDegrees, float squash, float brightness, float fleckOffsetX, float fleckOffsetY, bool squashAlongZ = false)
        {
            YawDegrees = yawDegrees;
            Squash = squash;
            Brightness = brightness;
            FleckOffsetX = fleckOffsetX;
            FleckOffsetY = fleckOffsetY;
            SquashAlongZ = squashAlongZ;
        }

        public static PieceLook Neutral => new PieceLook(0f, 1f, 1f, 0f, 0f);

        /// <summary>Placeholder for the showcase only: a deterministic hash of the id and the [pieces] dials.</summary>
        public static PieceLook FromId(int id, float yawDeg = 13f, float squashMin = 0.82f, float brightness = 0.06f)
        {
            uint s = (uint)id * 2654435761u + 12345u;
            float Next()
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                s *= 2246822519u;
                return (s >> 8) / 16777216f;
            }
            Next();
            float yaw = (Next() * 2f - 1f) * yawDeg;
            float squash = squashMin + Next() * (1f - squashMin);
            float bright = 1f + (Next() * 2f - 1f) * brightness;
            float fx = Next() * 2f - 1f;
            float fy = Next() * 2f - 1f;
            bool flip = Next() > 0.5f;
            return new PieceLook(yaw, squash, bright, fx, fy, flip);
        }
    }
}
