using Cascade.Config;

namespace Cascade.Presentation
{
    /// <summary>
    /// Everything <see cref="TimelinePlanner"/> needs to time and shape animations. The first block is
    /// the <c>[animation]</c> dials (milliseconds, unscaled); the second is the shape constants of the
    /// animator (they were literals; here they are parameters so the
    /// style can change them). <see cref="Speed"/> is the player animation speed.
    /// Build it with <see cref="From"/>; the tests use <see cref="Default"/> (the shipped dials).
    /// </summary>
    public sealed record TimingParams
    {
        // ---- [animation] dials (ms at speed 1.0)
        public double SelectLiftMs { get; init; } = 90;
        public double SwapMs { get; init; } = 180;
        public double SwapRejectMs { get; init; } = 220;
        public double ClearMs { get; init; } = 250;
        public double SpecialFireMs { get; init; } = 300;
        public double FallCellsPerSec { get; init; } = 14;
        public double SpawnDropMs { get; init; } = 160;
        public double LandSquash { get; init; } = 0.2;
        public int SettleWobbles { get; init; } = 1;
        public double WaveStaggerMs { get; init; } = 60;

        /// <summary>Player animation speed, already clamped (see <see cref="AnimationSettings"/>).</summary>
        public double Speed { get; init; } = 1.0;

        // ---- shape constants of the animator
        /// <summary>Peak <c>Hover</c> (world units) of a sliding piece during a swap.</summary>
        public double SwapHover { get; init; } = 0.06;
        /// <summary>Peak <c>Roll</c> (degrees) of the rock during a swap; the two pieces rock in opposite directions.</summary>
        public double SwapRockDeg { get; init; } = 7.0;
        /// <summary>How far toward the partner a rejected swap nudges (fraction of the distance).</summary>
        public double RejectNudge { get; init; } = 0.38;
        /// <summary>Peak <c>Roll</c> (degrees) of the shudder of a rejected swap.</summary>
        public double RejectRockDeg { get; init; } = 6.0;
        /// <summary>Peak <c>Roll</c> (degrees) of the settling wobble after a landing.</summary>
        public double LandRollDeg { get; init; } = 5.0;
        /// <summary>Land phase length: <c>LandBaseMs + LandPerWobbleMs * SettleWobbles</c> (ms at speed 1.0).</summary>
        public double LandBaseMs { get; init; } = 220;
        public double LandPerWobbleMs { get; init; } = 100;
        /// <summary>Rows above the board where a spawned piece appears; the k-th spawn into a cell in one round starts <see cref="SpawnStackStep"/> * k higher.</summary>
        public double SpawnHeight { get; init; } = 1.6;
        public double SpawnStackStep { get; init; } = 1.0;
        /// <summary>A moved piece stays "busy" (cannot start its next move) this fraction of <see cref="ClearMs"/> after it lands.</summary>
        public double LandBusyFraction { get; init; } = 0.6;
        /// <summary>The score popup of a clear starts this fraction of <see cref="ClearMs"/> into the wave.</summary>
        public double PopupDelayFraction { get; init; } = 0.3;
        /// <summary>A later wave starts <c>SpecialFireMs * WaveFireFraction + WaveStaggerMs</c> after the previous one.</summary>
        public double WaveFireFraction { get; init; } = 0.55;
        /// <summary>The clears of a round count as finished when its special fire is this fraction through.</summary>
        public double FireClearFraction { get; init; } = 0.7;
        /// <summary>Special creation pop: duration (ms at speed 1.0) and peak extra <c>Squash</c>.</summary>
        public double SpecialCreateMs { get; init; } = 250;
        public double SpecialPopSquash { get; init; } = 0.3;
        /// <summary>Shuffle squash: duration (ms at speed 1.0) and depth (the piece squashes to 1 - depth).</summary>
        public double ShuffleMs { get; init; } = 400;
        public double ShuffleSquash { get; init; } = 0.7;
        /// <summary>Score popup lifetime (ms at speed 1.0).</summary>
        public double PopupMs { get; init; } = 900;
        /// <summary>Hint pulse: total time (ms at speed 1.0), number of bounces and peak hover (world units).</summary>
        public double HintMs { get; init; } = 1600;
        public int HintPulses { get; init; } = 3;
        public double HintHover { get; init; } = 0.12;

        /// <summary>The combo celebration (PRD 5.4.1).</summary>
        public CelebrationParams Celebration { get; init; } = CelebrationParams.Default;

        /// <summary>A fixed time in ms, divided by the animation speed.</summary>
        public double Scaled(double ms) => ms / Speed;

        /// <summary>The shipped defaults at speed 1.0.</summary>
        public static readonly TimingParams Default = new TimingParams();

        /// <summary>Built by the composition root from the <c>[animation]</c> dials and the player speed.</summary>
        public static TimingParams From(DialRegistry dials, AnimationSettings? settings = null)
        {
            var a = dials.Animation;
            return new TimingParams
            {
                SelectLiftMs = a.SelectLift,
                SwapMs = a.Swap,
                SwapRejectMs = a.SwapReject,
                ClearMs = a.Clear,
                SpecialFireMs = a.SpecialFire,
                FallCellsPerSec = a.FallCellsPerSec,
                SpawnDropMs = a.SpawnDrop,
                LandSquash = a.LandSquash,
                SettleWobbles = a.SettleWobbles,
                WaveStaggerMs = a.WaveStagger,
                Celebration = CelebrationParams.From(dials),
                Speed = (settings ?? AnimationSettings.Default).ClampedSpeed,
            };
        }
    }
}
