using System;

namespace Cascade.Presentation
{
    /// <summary>
    /// The player's animation setting (<c>settings.animation_speed</c>, 0.5 to 3.0; 2.0 = twice as fast).
    /// It divides every duration and multiplies the fall speed (TRD 12.3). Immutable; build a new
    /// <see cref="TimingParams"/> when the player changes it.
    /// </summary>
    public sealed record AnimationSettings(double Speed = 1.0)
    {
        public const double MinSpeed = 0.5;
        public const double MaxSpeed = 3.0;

        public static readonly AnimationSettings Default = new AnimationSettings(1.0);

        /// <summary>The speed clamped to <see cref="MinSpeed"/>..<see cref="MaxSpeed"/> (NaN counts as 1).</summary>
        public double ClampedSpeed => double.IsNaN(Speed) ? 1.0 : Math.Min(MaxSpeed, Math.Max(MinSpeed, Speed));
    }
}
