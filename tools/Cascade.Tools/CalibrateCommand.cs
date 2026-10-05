using System;
using System.Collections.Generic;
using System.Globalization;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>calibrate</c>: suggests <c>[params.score_per_move]</c> / <c>[params.collect_per_move]</c>
    /// values (the calibration tool). For sample levels it plays the bot with an
    /// unreachable goal for the level's move limit and finds the per-move rate at which a goal of
    /// size 1.0 would be won at the level's target win rate.
    /// </summary>
    public static class CalibrateCommand
    {
        static string Pad(string s, int width) => s.PadRight(width);

        public static int Run(Args args)
        {
            int step = args.Int("step", 5), max = args.Int("max", 150), seeds = args.Int("seeds", 3), runs = args.Int("runs", 10);
            var env = Env.Load(args);
            var rules = env.Rules;
            var settings = env.Settings;
            var o = Console.Out;
            o.WriteLine("n  colors size holes moves amount target | score/move  tiles/move  (implied rates)");

            var levels = new SortedSet<int> { 1 };
            for (int n = step; n <= max; n += step) levels.Add(n);
            foreach (int n in levels)
            {
                var p = env.Profile.Evaluate(n);
                var scores = new List<int>();
                var tiles = new List<int>();
                int holes = 0;
                for (int s = 0; s < seeds; s++)
                {
                    var candidate = LevelGenerator.MakeCandidate(p, (ulong)(1000 * n + s), settings);
                    var lvl = candidate with { Goals = new[] { Goal.Score(1_000_000_000) } };
                    holes = lvl.Holes.Count;
                    var baseBoard = Bot.StartBoard(lvl);
                    for (int r = 0; r < runs; r++)
                    {
                        var streams = Bot.RunStreams(lvl.Seed, r);
                        var session = new Session(lvl, rules, settings.Weights, streams, baseBoard.Copy());
                        var rng = streams.Bot;
                        while (session.Status == SessionStatus.Playing)
                        {
                            IReadOnlyList<(CellId A, CellId B)> choices = rng.Random() < settings.BotRandomChance ? session.LegalMoves : session.BestMoves();
                            var (a, b) = rng.Choice(choices);
                            session.TrySwap(a, b);
                        }
                        scores.Add(session.Score);
                        tiles.Add(session.TilesCollected);
                    }
                }
                scores.Sort();
                tiles.Sort();
                double q = 100 * (1 - p.TargetWinRate);
                double denom = p.MoveLimit * p.GoalAmount;
                var ic = CultureInfo.InvariantCulture;
                o.WriteLine(Pad(n.ToString(ic), 4) + Pad(p.Colors.ToString(ic), 7) + Pad(p.BoardSize.ToString(ic), 5) + Pad(holes.ToString(ic), 6) +
                            Pad(p.MoveLimit.ToString(ic), 6) + Pad(p.GoalAmount.ToString("0.00", ic), 7) + Pad(p.TargetWinRate.ToString("0.00", ic), 7) + "| " +
                            (Tuner.Percentile(scores, q) / denom).ToString("0", ic).PadLeft(9) + " " +
                            (Tuner.Percentile(tiles, q) / denom).ToString("0.00", ic).PadLeft(10));
            }
            return 0;
        }
    }
}
