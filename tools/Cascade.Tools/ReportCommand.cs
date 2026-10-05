using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cascade.Core;
using Cascade.Generation;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>report &lt;from&gt; &lt;to&gt;</c>: generates levels, playtests them with the bot and writes a CSV
    /// of target vs measured win rate (the difficulty report), then prints the
    /// generation times that TRD §7 compares with the reference baseline.
    /// </summary>
    public static class ReportCommand
    {
        sealed class Row
        {
            public int Level;
            public double Difficulty, Target, Measured, Ms;
            public int MoveLimit, Rounds, BotRuns, BotMoves, WithinTolerance, OverBudget;
            public string Goals = "";
        }

        static string Describe(Level level) => string.Join(" + ", level.Goals.Select(g =>
            (g.Kind == GoalKind.Score ? "score" : "collect") + ":" + g.Target.ToString(CultureInfo.InvariantCulture) +
            (g.Colors.Count > 0 ? "@" + string.Join("/", g.Colors) : "")));

        static double Round(double v, int digits) => Math.Round(v, digits, MidpointRounding.ToEven);

        static string F(double v)
        {
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            return s.IndexOfAny(new[] { '.', 'E' }) >= 0 ? s : s + ".0"; // round-trip form with a trailing ".0"
        }

        public static int Run(Args args)
        {
            int from = args.PositionalInt(0, "from");
            int to = args.PositionalInt(1, "to");
            if (from < 1 || to < from) throw new UsageException("expected 1 <= from <= to");
            int seedBase = args.Int("seed", 1);
            string outPath = args.Get("out") ?? Path.Combine("build", "difficulty.csv");

            var env = Env.Load(args);
            var clock = new SystemStopwatch();
            var source = new GeneratingLevelSource(env.Dials, env.Profile, clock);
            int budget = env.Settings.BudgetMs;
            double tol = env.Settings.WinRateTolerance;

            var rows = new List<Row>();
            double t0 = clock.ElapsedMilliseconds;
            for (int n = from; n <= to; n++)
            {
                var level = source.Get(n, (ulong)seedBase * 1_000_003UL + (ulong)n);
                var stats = source.LastStats!;
                rows.Add(new Row
                {
                    Level = n,
                    Difficulty = Round(level.Difficulty, 3),
                    Target = Round(level.TargetWinRate, 3),
                    Measured = Round(level.MeasuredWinRate!.Value, 3),
                    MoveLimit = level.MoveLimit,
                    Goals = Describe(level),
                    Ms = Round(stats.Ms, 1),
                    Rounds = stats.Rounds,
                    BotRuns = stats.BotRuns,
                    BotMoves = stats.BotMoves,
                    WithinTolerance = stats.WithinTolerance ? 1 : 0,
                    OverBudget = stats.OverBudget ? 1 : 0,
                });
            }
            double totalMs = clock.ElapsedMilliseconds - t0;

            string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (dir != null) Directory.CreateDirectory(dir);
            var csv = new StringBuilder("level,difficulty,target,measured,move_limit,goals,ms,rounds,bot_runs,within_tolerance,over_budget\n");
            foreach (var r in rows)
                csv.Append(r.Level).Append(',').Append(F(r.Difficulty)).Append(',').Append(F(r.Target)).Append(',').Append(F(r.Measured)).Append(',')
                   .Append(r.MoveLimit).Append(',').Append(r.Goals).Append(',').Append(F(r.Ms)).Append(',').Append(r.Rounds).Append(',')
                   .Append(r.BotRuns).Append(',').Append(r.WithinTolerance).Append(',').Append(r.OverBudget).Append('\n');
            File.WriteAllText(outPath, csv.ToString(), new UTF8Encoding(false));

            int inside = rows.Count(r => Math.Abs(r.Measured - r.Target) <= tol);
            var ms = rows.Select(r => r.Ms).OrderBy(x => x).ToList();
            double mean = totalMs / rows.Count;
            double median = ms[ms.Count / 2];
            double p95 = ms[(int)(ms.Count * 0.95)];
            double max = ms[ms.Count - 1];
            int overMs = budget > 0 ? rows.Count(r => r.Ms > budget) : 0;
            var o = Console.Out;
            o.WriteLine("levels: " + rows.Count + " (" + from + ".." + to + ")   total: " + (totalMs / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " s   mean: " + mean.ToString("0.0", CultureInfo.InvariantCulture) + " ms/level");
            o.WriteLine("median: " + median.ToString("0.0", CultureInfo.InvariantCulture) + " ms   p95: " + p95.ToString("0.0", CultureInfo.InvariantCulture) + " ms   max: " + max.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
            o.WriteLine("within tolerance (+-" + F(tol) + "): " + inside + "/" + rows.Count + " (" + (100.0 * inside / rows.Count).ToString("0", CultureInfo.InvariantCulture) + " %)   over budget (stats flag): " + rows.Sum(r => r.OverBudget));
            long botMoves = rows.Sum(r => (long)r.BotMoves);
            o.WriteLine("budget_ms: " + budget + (budget > 0 ? "   levels slower than the budget: " + overMs : " (no cap)") +
                        "   bot moves: " + botMoves + " (" + (botMoves / (totalMs / 1000)).ToString("0", CultureInfo.InvariantCulture) + " per second)");
            o.WriteLine("wrote " + Path.GetFullPath(outPath));
            return 0;
        }
    }
}
