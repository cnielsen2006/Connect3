using System;
using System.Globalization;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Tests.Generation;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>deadlock-check</c>: the MRD §5 criterion "0 unsolvable or deadlocked levels across 10,000
    /// generated levels". By default each level is the validated candidate (the board is fixed there;
    /// the tuner only changes the move limit, goals and stars); <c>--full</c> runs the whole pipeline
    /// including the bot for every level, which is slow.
    /// </summary>
    public static class DeadlockCommand
    {
        public static int Run(Args args)
        {
            int levels = args.Int("levels", 10_000), moves = args.Int("moves", 30);
            bool full = args.Has("full");
            var env = Env.Load(args);
            var rules = env.Rules;
            var settings = env.Settings;
            var clock = new SystemStopwatch();
            Func<int, ulong, Core.Level> generate;
            if (full)
            {
                var source = new GeneratingLevelSource(env.Dials, env.Profile, clock);
                generate = (n, seed) => source.Get(n, seed);
            }
            else generate = (n, seed) => LevelGenerator.MakeCandidate(env.Profile.Evaluate(n), seed, settings);

            double t0 = clock.ElapsedMilliseconds;
            int lastPercent = -1;
            var result = DeadlockCheck.Run(levels, moves, rules, generate, done =>
            {
                int percent = done * 100 / levels;
                if (percent != lastPercent && percent % 10 == 0) { Console.Error.WriteLine("  " + percent + " %"); lastPercent = percent; }
            });
            double seconds = (clock.ElapsedMilliseconds - t0) / 1000;
            var o = Console.Out;
            o.WriteLine("levels checked: " + result.Levels + " of " + levels + (full ? " (full pipeline)" : " (validated candidates)") +
                        "   moves played: " + result.Moves + "   auto-shuffles: " + result.Shuffles + "   time: " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            if (result.Clean)
            {
                o.WriteLine("clean: 0 deadlocked or invalid levels");
                return 0;
            }
            o.WriteLine("FAILURES: " + result.Failures.Count);
            for (int i = 0; i < Math.Min(20, result.Failures.Count); i++) o.WriteLine("  " + result.Failures[i]);
            return 1;
        }
    }
}
