using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Gameplay
{
    /// <summary>Dials to immutable snapshots (TRD §3, §5).</summary>
    public class RuleSetsTests
    {
        [Test]
        public void Default_dials_give_the_default_rule_set()
        {
            var rules = RuleSets.From(TestDials.Default());
            Assert.That(rules, Is.EqualTo(RuleSet.Default));
            Assert.That(rules.Scoring.Run3, Is.EqualTo(60));
            Assert.That(rules.Specials.IncomingChance, Is.EqualTo(0.4));
        }

        [Test]
        public void Default_dials_give_the_default_generator_settings()
        {
            Assert.That(RuleSets.GeneratorFrom(TestDials.Default()), Is.EqualTo(GeneratorSettings.Default));
            Assert.That(RuleSets.WeightsFrom(TestDials.Default()), Is.EqualTo(new MoveWeights()));
        }

        [Test]
        public void Snapshots_do_not_follow_later_dial_changes_but_a_new_snapshot_does()
        {
            var dials = TestDials.Default();
            var before = RuleSets.From(dials);
            var settingsBefore = RuleSets.GeneratorFrom(dials);
            dials.Set("scoring", "run3", 90);
            dials.Set("specials", "bomb_radius", 2);
            dials.Set("generator", "bot_runs", 7);
            dials.Set("generator", "bot_weight_goal", 4.5);
            dials.Set("stars", "min_wins", 1);
            Assert.That(before.Scoring.Run3, Is.EqualTo(60));
            Assert.That(settingsBefore.BotRuns, Is.EqualTo(20));
            var after = RuleSets.From(dials);
            Assert.That(after.Scoring.Run3, Is.EqualTo(90));
            Assert.That(after.Specials.BombRadius, Is.EqualTo(2));
            var settings = RuleSets.GeneratorFrom(dials);
            Assert.That(settings.BotRuns, Is.EqualTo(7));
            Assert.That(settings.Weights.Goal, Is.EqualTo(4.5));
            Assert.That(settings.Stars.MinWins, Is.EqualTo(1));
            Assert.That(settings, Is.Not.EqualTo(settingsBefore));
        }

        [Test]
        public void Every_generator_and_stars_dial_reaches_the_snapshot()
        {
            var dials = TestDials.Load(d =>
            {
                d.Set("generator", "tune_rounds", 2);
                d.Set("generator", "win_rate_tolerance", 0.1);
                d.Set("generator", "budget_ms", 250);
                d.Set("generator", "min_bot_runs", 3);
                d.Set("generator", "bot_random_chance", 0.5);
                d.Set("generator", "bot_weight_special", 1.5);
                d.Set("generator", "bot_weight_points", 0.02);
                d.Set("generator", "move_limit_min", 12);
                d.Set("generator", "move_limit_max", 40);
                d.Set("generator", "goal_step", 0.2);
                d.Set("generator", "goal_score_round", 25);
                d.Set("generator", "collect_min", 4);
                d.Set("generator", "collect_two_color_chance", 0.1);
                d.Set("generator", "combo_scale", 0.5);
                d.Set("generator", "holes_chance", 0.3);
                d.Set("generator", "min_cells", 30);
                d.Set("stars", "two_star_percentile", 40);
                d.Set("stars", "three_star_percentile", 90);
                d.Set("stars", "fallback_two", 1.1);
                d.Set("stars", "fallback_three", 1.4);
            });
            var s = RuleSets.GeneratorFrom(dials);
            Assert.That(s.TuneRounds, Is.EqualTo(2));
            Assert.That(s.WinRateTolerance, Is.EqualTo(0.1));
            Assert.That(s.BudgetMs, Is.EqualTo(250));
            Assert.That(s.MinBotRuns, Is.EqualTo(3));
            Assert.That(s.BotRandomChance, Is.EqualTo(0.5));
            Assert.That(s.Weights, Is.EqualTo(new MoveWeights { Goal = 10.0, Special = 1.5, Points = 0.02 }));
            Assert.That(s.MoveLimitMin, Is.EqualTo(12));
            Assert.That(s.MoveLimitMax, Is.EqualTo(40));
            Assert.That(s.GoalStep, Is.EqualTo(0.2));
            Assert.That(s.GoalScoreRound, Is.EqualTo(25));
            Assert.That(s.CollectMin, Is.EqualTo(4));
            Assert.That(s.CollectTwoColorChance, Is.EqualTo(0.1));
            Assert.That(s.ComboScale, Is.EqualTo(0.5));
            Assert.That(s.HolesChance, Is.EqualTo(0.3));
            Assert.That(s.MinCells, Is.EqualTo(30));
            Assert.That(s.Stars, Is.EqualTo(new StarSettings
            {
                TwoStarPercentile = 40, ThreeStarPercentile = 90, MinWins = 3, FallbackTwo = 1.1, FallbackThree = 1.4,
            }));
        }
    }
}
