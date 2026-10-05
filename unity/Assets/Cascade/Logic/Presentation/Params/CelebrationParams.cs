using Cascade.Config;

namespace Cascade.Presentation
{
    /// <summary>
    /// The combo celebration (PRD 5.4.1, the <c>[celebration]</c> dials): the multiplier tiers and what each tier adds.
    /// <see cref="Default"/> equals the shipped dials.
    /// </summary>
    public sealed record CelebrationParams
    {
        /// <summary>Multipliers at which tiers 1 to 4 start (ascending).</summary>
        public double Tier1 { get; init; } = 1.5;
        public double Tier2 { get; init; } = 2.5;
        public double Tier3 { get; init; } = 4.0;
        public double Tier4 { get; init; } = 6.0;
        public double BadgeScaleBase { get; init; } = 1.0;
        public double BadgeScaleStep { get; init; } = 0.35;
        /// <summary>Badge lifetime (ms at speed 1.0).</summary>
        public double BadgeMs { get; init; } = 700;
        /// <summary>Delay between the badges of groups of one round (ms at speed 1.0).</summary>
        public double GroupStaggerMs { get; init; } = 140;
        public int ParticlesPerTier { get; init; } = 10;
        /// <summary>Camera zoom-in per tier (fraction of the view).</summary>
        public double CameraPunchPerTier { get; init; } = 0.012;
        public int StingerFromTier { get; init; } = 2;
        public bool ReduceEffects { get; init; }

        public static readonly CelebrationParams Default = new CelebrationParams();

        /// <summary>0 below <see cref="Tier1"/>, then 1 to 4 as each threshold is reached.</summary>
        public int TierOf(double multiplier)
            => multiplier >= Tier4 ? 4 : multiplier >= Tier3 ? 3 : multiplier >= Tier2 ? 2 : multiplier >= Tier1 ? 1 : 0;

        /// <summary>Badge size at a tier.</summary>
        public double BadgeScale(int tier) => BadgeScaleBase + BadgeScaleStep * tier;

        public static CelebrationParams From(DialRegistry dials)
        {
            var c = dials.Celebration;
            return new CelebrationParams
            {
                Tier1 = c.Tier1, Tier2 = c.Tier2, Tier3 = c.Tier3, Tier4 = c.Tier4,
                BadgeScaleBase = c.BadgeScaleBase, BadgeScaleStep = c.BadgeScaleStep,
                BadgeMs = c.BadgeMs, GroupStaggerMs = c.GroupStaggerMs,
                ParticlesPerTier = c.ParticlesPerTier, CameraPunchPerTier = c.CameraPunchPerTier,
                StingerFromTier = c.StingerFromTier, ReduceEffects = c.ReduceEffects != 0,
            };
        }
    }
}
