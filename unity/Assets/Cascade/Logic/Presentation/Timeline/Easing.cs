using System;

namespace Cascade.Presentation
{
    /// <summary>
    /// The shapes a <see cref="Tween"/> can take. A tween's value is <c>From + (To - From) * f(u)</c> with
    /// <c>u</c> the progress 0..1 and <c>f</c> the shape below (<see cref="Easings.Eval"/>). The "returning"
    /// shapes end at 0, so the property comes back to <c>From</c> (a bump, a wobble); the others end at 1.
    /// </summary>
    public enum Easing
    {
        /// <summary>f = u.</summary>
        Linear,
        /// <summary>f = u*u*(3-2u): slides, select lift.</summary>
        SmoothStep,
        /// <summary>f = u*u: a pebble sinking into the tray.</summary>
        QuadIn,
        /// <summary>f = 0.35u + 0.65u*u: a dropped stone (starts at 35% speed, accelerates). Falls and spawn drops.</summary>
        FallAccel,
        /// <summary>f = sin(pi u): up and back (returns). Swap hover and rock, special pop, shuffle squash.</summary>
        Bump,
        /// <summary>f = sin(pi u)^1.5 (returns): the nudge of a rejected swap.</summary>
        BumpPow15,
        /// <summary>f = sin(pi u P) * (1 - u), P = <see cref="Tween.EasingParam"/> (returns): the settling wobble after a landing.</summary>
        Wobble,
        /// <summary>f = 0.5 - 0.5 cos(2 pi P u), P = <see cref="Tween.EasingParam"/> pulses (returns): the hint pulse.</summary>
        Pulse,
    }

    /// <summary>Evaluates <see cref="Easing"/> shapes.</summary>
    public static class Easings
    {
        /// <summary>The shape value for progress <paramref name="u"/> (clamped to 0..1).</summary>
        public static double Eval(Easing easing, double u, double param = 0)
        {
            u = u < 0 ? 0 : (u > 1 ? 1 : u);
            switch (easing)
            {
                case Easing.Linear: return u;
                case Easing.SmoothStep: return u * u * (3 - 2 * u);
                case Easing.QuadIn: return u * u;
                case Easing.FallAccel: return 0.35 * u + 0.65 * u * u;
                case Easing.Bump: return u >= 1 ? 0 : Math.Sin(Math.PI * u);
                case Easing.BumpPow15: return u >= 1 ? 0 : Math.Pow(Math.Sin(Math.PI * u), 1.5);
                case Easing.Wobble: return u >= 1 ? 0 : Math.Sin(Math.PI * u * param) * (1 - u);
                case Easing.Pulse: return u >= 1 ? 0 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * param * u);
                default: throw new ArgumentOutOfRangeException(nameof(easing));
            }
        }
    }
}
