namespace Cascade.Gameplay
{
    /// <summary>
    /// The player's settings (TRD §8, §12.3). Immutable. It lives in Gameplay, not Persistence, so
    /// Presentation view-models can use it (Presentation may not reference Persistence);
    /// <c>Cascade.Persistence.SaveService</c> reads and writes it.
    /// </summary>
    public sealed record PlayerSettings
    {
        /// <summary>Smallest <see cref="AnimationSpeed"/>.</summary>
        public const double MinAnimationSpeed = 0.5;
        /// <summary>Largest <see cref="AnimationSpeed"/>.</summary>
        public const double MaxAnimationSpeed = 3.0;

        /// <summary>Stored but without effect until sound exists (U8).</summary>
        public bool Sound { get; init; } = true;

        /// <summary>Divides every animation duration; 1.0 is normal, 2.0 twice as fast. Range 0.5-3.0.</summary>
        public double AnimationSpeed { get; init; } = 1.0;

        /// <summary>Adaptive difficulty on or off (stored; no effect until U8).</summary>
        public bool Adaptive { get; init; } = true;

        /// <summary>True if <paramref name="speed"/> is a valid <see cref="AnimationSpeed"/>.</summary>
        public static bool IsValidSpeed(double speed) =>
            !double.IsNaN(speed) && speed >= MinAnimationSpeed && speed <= MaxAnimationSpeed;
    }
}
