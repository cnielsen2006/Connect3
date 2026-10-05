using System;

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

        /// <summary>Frame-rate cap choices the player can pick (the settings page offers 30 or 60).</summary>
        public const int LowFps = 30, HighFps = 60;

        /// <summary>The frame-rate cap: <see cref="LowFps"/> (saves battery) or <see cref="HighFps"/>.</summary>
        public int TargetFps { get; init; } = HighFps;

        /// <summary>Art style ids, in the order the settings page cycles them (docs/art: K Pebbles, I Claymation, J Studio Set).</summary>
        public static readonly string[] Skins = { "K", "I", "J" };

        /// <summary>The art style (one of <see cref="Skins"/>). It applies at once.</summary>
        public string Skin { get; init; } = "K";

        /// <summary>True if <paramref name="skin"/> is a known art style id.</summary>
        public static bool IsValidSkin(string? skin) => skin != null && Array.IndexOf(Skins, skin) >= 0;

        /// <summary>True if <paramref name="fps"/> is a frame rate the player may choose.</summary>
        public static bool IsValidFps(int fps) => fps == LowFps || fps == HighFps;

        /// <summary>True if <paramref name="speed"/> is a valid <see cref="AnimationSpeed"/>.</summary>
        public static bool IsValidSpeed(double speed) =>
            !double.IsNaN(speed) && speed >= MinAnimationSpeed && speed <= MaxAnimationSpeed;
    }
}
