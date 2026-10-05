using System;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Platform.Abstractions;

namespace Cascade.Generation
{
    /// <summary>What a tuning pass did (the numbers behind <see cref="GenStats"/>).</summary>
    public sealed class TuneStats
    {
        /// <summary>Measurements made.</summary>
        public int Rounds { get; set; }
        public int BotRuns { get; set; }
        public int BotMoves { get; set; }
        public bool WithinTolerance { get; set; }
        public bool OverBudget { get; set; }
    }

    /// <summary>
    /// Tuner (TRD §7 step 6): playtest with the bot, nudge move_limit (then goal size) toward the
    /// target win rate, keep the closest candidate, and set star thresholds from the bot's scores.
    /// </summary>
    public static class Tuner
    {
        /// <summary>Linear-interpolated percentile of an ascending list.</summary>
        public static double Percentile(IReadOnlyList<int> sortedValues, double pct)
        {
            if (sortedValues.Count == 0) throw new ArgumentException("percentile of nothing");
            double pos = pct / 100 * (sortedValues.Count - 1);
            int lo = (int)pos;
            int hi = Math.Min(lo + 1, sortedValues.Count - 1);
            return sortedValues[lo] + (sortedValues[hi] - sortedValues[lo]) * (pos - lo);
        }

        /// <summary>
        /// (star2, star3): percentiles of the bot's winning scores; a fallback from the median of all
        /// runs when the bot (almost) never wins.
        /// </summary>
        public static (int Two, int Three) StarThresholds(Measurement m, GeneratorSettings g)
        {
            var s = g.Stars;
            var wins = m.WinScores;
            int two, three;
            if (wins.Count >= s.MinWins)
            {
                two = MathX.RoundHalfEven(Percentile(wins, s.TwoStarPercentile));
                three = MathX.RoundHalfEven(Percentile(wins, s.ThreeStarPercentile));
            }
            else
            {
                double median = m.Runs.Count > 0 ? Percentile(m.AllScores, 50) : 0;
                two = MathX.RoundHalfEven(median * s.FallbackTwo);
                three = MathX.RoundHalfEven(median * s.FallbackThree);
            }
            two = Math.Max(1, two);
            return (two, Math.Max(two + 1, three));
        }

        /// <summary>
        /// Too easy: fewer moves; too hard: more moves (1-3 per step, by how far off). Once
        /// move_limit is at a bound, resize the goals instead.
        /// </summary>
        public static Level Adjust(Level level, double winRate, GeneratorSettings g)
        {
            double err = winRate - level.TargetWinRate; // > 0: too easy
            int steps = Math.Max(1, Math.Min(3, MathX.RoundHalfEven(Math.Abs(err) * 10)));
            int newMoves = err > 0 ? level.MoveLimit - steps : level.MoveLimit + steps;
            if (g.MoveLimitMin <= newMoves && newMoves <= g.MoveLimitMax) return level with { MoveLimit = newMoves };
            int clamped = Math.Max(g.MoveLimitMin, Math.Min(g.MoveLimitMax, newMoves));
            if (clamped != level.MoveLimit) return level with { MoveLimit = clamped };
            double factor = err > 0 ? 1 + g.GoalStep * steps : 1 / (1 + g.GoalStep * steps);
            return level with { Goals = LevelGenerator.ScaleGoals(level.Goals, factor, g) };
        }

        /// <summary>
        /// Returns the tuned level (<c>MeasuredWinRate</c> and stars filled in) and stats.
        /// <c>BudgetMs</c> &gt; 0 caps wall time from <paramref name="startMs"/> (a reading of
        /// <paramref name="clock"/>; default: now): no new round or run begins after it and the closest
        /// candidate so far is kept. 0 disables the cap, which makes the result a pure function of
        /// (level number, seed).
        /// </summary>
        public static (Level Level, TuneStats Stats) Tune(Level candidate, RuleSet rules, GeneratorSettings g,
            IStopwatch? clock = null, double? startMs = null)
        {
            double? deadline = null;
            if (g.BudgetMs > 0 && clock != null) deadline = (startMs ?? clock.ElapsedMilliseconds) + g.BudgetMs;
            var stats = new TuneStats();
            var baseBoard = Bot.StartBoard(candidate);
            var level = candidate;
            var seen = new List<(int MoveLimit, ValueList<Goal> Goals)>();
            double bestDist = 0;
            Level? bestLevel = null;
            Measurement? bestMeasurement = null;
            for (int round = 0; round <= g.TuneRounds; round++)
            {
                var m = Bot.Measure(level, rules, g, g.BotRuns, baseBoard, deadline, clock);
                stats.Rounds++;
                stats.BotRuns += m.Runs.Count;
                stats.BotMoves += m.Moves;
                double dist = Math.Abs(m.WinRate - level.TargetWinRate);
                if (bestLevel == null || dist < bestDist)
                {
                    bestDist = dist;
                    bestLevel = level;
                    bestMeasurement = m;
                }
                seen.Add((level.MoveLimit, level.Goals));
                if (dist <= g.WinRateTolerance)
                {
                    stats.WithinTolerance = true;
                    break;
                }
                if (deadline.HasValue && clock!.ElapsedMilliseconds > deadline.Value)
                {
                    stats.OverBudget = true;
                    break;
                }
                level = Adjust(level, m.WinRate, g);
                bool repeated = false;
                foreach (var (ml, goals) in seen)
                    if (ml == level.MoveLimit && goals == level.Goals) { repeated = true; break; }
                if (repeated) break;
            }
            var (two, three) = StarThresholds(bestMeasurement!, g);
            var tuned = bestLevel! with { MeasuredWinRate = bestMeasurement!.WinRate, Star2 = two, Star3 = three };
            return (tuned, stats);
        }
    }
}
