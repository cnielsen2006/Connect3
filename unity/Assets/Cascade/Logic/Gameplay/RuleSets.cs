using Cascade.Config;
using Cascade.Core;

namespace Cascade.Gameplay
{
    /// <summary>Bot / hint move value weights (the <c>bot_weight_*</c> dials).</summary>
    public sealed record MoveWeights
    {
        /// <summary>Per tile of a wanted color cleared (collect goal progress).</summary>
        public double Goal { get; init; } = 10.0;
        /// <summary>Per special earned.</summary>
        public double Special { get; init; } = 3.0;
        /// <summary>Per point.</summary>
        public double Points { get; init; } = 0.01;
    }

    /// <summary>The <c>[stars]</c> dials as an immutable snapshot.</summary>
    public sealed record StarSettings
    {
        public int TwoStarPercentile { get; init; } = 50;
        public int ThreeStarPercentile { get; init; } = 85;
        /// <summary>Fewer bot wins than this and the fallback thresholds are used.</summary>
        public int MinWins { get; init; } = 3;
        public double FallbackTwo { get; init; } = 1.25;
        public double FallbackThree { get; init; } = 1.6;
    }

    /// <summary>
    /// The <c>[generator]</c> and <c>[stars]</c> dials as one immutable snapshot (TRD §5), which is
    /// all level generation reads. The defaults equal the shipped <c>dials.toml</c>. Two snapshots
    /// of the same dials are equal (value equality), so a holder can tell whether a dial changed.
    /// </summary>
    public sealed record GeneratorSettings
    {
        public int BotRuns { get; init; } = 20;
        public int TuneRounds { get; init; } = 3;
        public double WinRateTolerance { get; init; } = 0.08;
        /// <summary>Wall-clock cap per level in ms; 0 = no cap.</summary>
        public int BudgetMs { get; init; } = 1000;
        public int MinBotRuns { get; init; } = 5;
        public double BotRandomChance { get; init; } = 0.25;
        public MoveWeights Weights { get; init; } = new MoveWeights();
        public int MoveLimitMin { get; init; } = 10;
        public int MoveLimitMax { get; init; } = 50;
        public double GoalStep { get; init; } = 0.1;
        public int GoalScoreRound { get; init; } = 50;
        public int CollectMin { get; init; } = 6;
        public double CollectTwoColorChance { get; init; } = 0.4;
        public double ComboScale { get; init; } = 0.75;
        public double HolesChance { get; init; } = 0.8;
        public int MinCells { get; init; } = 36;
        public StarSettings Stars { get; init; } = new StarSettings();

        /// <summary>The defaults (an immutable shared instance).</summary>
        public static readonly GeneratorSettings Default = new GeneratorSettings();
    }

    /// <summary>
    /// Turns the dial registry into the immutable snapshots the rules and the generator take
    /// (TRD §3, §5). Call again after a dial change: the new snapshot applies from the next move
    /// (<see cref="Session.Rules"/>) or the next level.
    /// </summary>
    public static class RuleSets
    {
        /// <summary>The <see cref="RuleSet"/> (scoring and specials) for <see cref="Rules.Resolve"/>.</summary>
        public static RuleSet From(DialRegistry dials)
        {
            var s = dials.Scoring;
            var p = dials.Specials;
            return new RuleSet
            {
                Scoring = new ScoringRules
                {
                    Run3 = s.Run3,
                    Run4 = s.Run4,
                    Run5 = s.Run5,
                    RunExtraPerTile = s.RunExtraPerTile,
                    Cross = s.Cross,
                    SpecialTileCleared = s.SpecialTileCleared,
                    CascadeStep = s.CascadeStep,
                    GroupStep = s.GroupStep,
                    ComboColorColor = s.ComboColorColor,
                },
                Specials = new SpecialRules { IncomingChance = p.IncomingChance, BombRadius = p.BombRadius },
            };
        }

        /// <summary>The bot / hint move value weights.</summary>
        public static MoveWeights WeightsFrom(DialRegistry dials)
        {
            var g = dials.Generator;
            return new MoveWeights { Goal = g.BotWeightGoal, Special = g.BotWeightSpecial, Points = g.BotWeightPoints };
        }

        /// <summary>The planner settings (<c>[planner]</c> dials).</summary>
        public static PlannerSettings PlannerFrom(DialRegistry dials)
        {
            var p = dials.Planner;
            return new PlannerSettings
            {
                Depth = p.Depth, Width = p.Width, MaxNodes = p.MaxNodes, WinBonus = p.WinBonus,
                MovesLeftWeight = p.MovesLeftWeight, ProgressWeight = p.ProgressWeight, ScoreWeight = p.ScoreWeight,
                MaxProbes = p.MaxProbes,
            };
        }

        /// <summary>The <see cref="GeneratorSettings"/> (generator and stars dials).</summary>
        public static GeneratorSettings GeneratorFrom(DialRegistry dials)
        {
            var g = dials.Generator;
            var s = dials.Stars;
            return new GeneratorSettings
            {
                BotRuns = g.BotRuns,
                TuneRounds = g.TuneRounds,
                WinRateTolerance = g.WinRateTolerance,
                BudgetMs = g.BudgetMs,
                MinBotRuns = g.MinBotRuns,
                BotRandomChance = g.BotRandomChance,
                Weights = WeightsFrom(dials),
                MoveLimitMin = g.MoveLimitMin,
                MoveLimitMax = g.MoveLimitMax,
                GoalStep = g.GoalStep,
                GoalScoreRound = g.GoalScoreRound,
                CollectMin = g.CollectMin,
                CollectTwoColorChance = g.CollectTwoColorChance,
                ComboScale = g.ComboScale,
                HolesChance = g.HolesChance,
                MinCells = g.MinCells,
                Stars = new StarSettings
                {
                    TwoStarPercentile = s.TwoStarPercentile,
                    ThreeStarPercentile = s.ThreeStarPercentile,
                    MinWins = s.MinWins,
                    FallbackTwo = s.FallbackTwo,
                    FallbackThree = s.FallbackThree,
                },
            };
        }
    }
}
