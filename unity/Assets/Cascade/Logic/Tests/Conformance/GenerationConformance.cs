using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Platform.Abstractions;

namespace Cascade.Tests.Conformance
{
    /// <summary>What a conformance verification found. Plain data, so NUnit and the console tool can both use it.</summary>
    public sealed class ConformanceReport
    {
        /// <summary>Individual comparisons made (fields, values).</summary>
        public int Checks { get; set; }

        /// <summary>Doubles that were equal only within 1e-12, not <c>==</c> (TRD §9.2): reported, not failed.</summary>
        public List<string> Inexact { get; } = new List<string>();

        public List<string> Failures { get; } = new List<string>();

        public bool Passed => Failures.Count == 0;

        public void Merge(ConformanceReport other)
        {
            Checks += other.Checks;
            Inexact.AddRange(other.Inexact);
            Failures.AddRange(other.Failures);
        }
    }

    /// <summary>
    /// Verifies the U3 fixtures of <c>conformance/</c> (TRD §9.2): <c>profiles/evaluations.json</c>,
    /// <c>levels/*.json</c> and <c>bot/runs.json</c>, against the C# port. Fixtures are never
    /// regenerated from C#. Levels and bot runs were exported with <c>budget_ms = 0</c> and the
    /// default dials. No NUnit here: <c>tools/Cascade.Tools verify conformance</c> runs the same code.
    /// </summary>
    public sealed class GenerationConformance
    {
        public const double DoubleTolerance = 1e-12;

        readonly Func<string[], string> _readText;
        readonly Func<string, string[]> _listFiles;
        readonly Profile _profile;
        readonly RuleSet _rules;
        readonly GeneratorSettings _settings;
        readonly GeneratingLevelSource _source;

        /// <param name="readText">Reads a fixture, for example <c>parts =&gt; ConformanceFiles.ReadText(parts)</c>.</param>
        /// <param name="listFiles">Lists the file names of a fixture folder, sorted ordinally.</param>
        /// <param name="dials">Dials with <c>budget_ms = 0</c> and every other dial at its default.</param>
        public GenerationConformance(Func<string[], string> readText, Func<string, string[]> listFiles, Profile profile, DialRegistry dials)
        {
            _readText = readText;
            _listFiles = listFiles;
            _profile = profile;
            _rules = RuleSets.From(dials);
            _settings = RuleSets.GeneratorFrom(dials);
            if (_settings.BudgetMs != 0) throw new ArgumentException("the fixtures were exported with budget_ms = 0");
            _source = new GeneratingLevelSource(dials, profile, new NoClock());
        }

        /// <summary>A stopwatch that never advances (the budget is 0, so it is never consulted).</summary>
        sealed class NoClock : IStopwatch { public double ElapsedMilliseconds => 0; }

        // -- helpers ---------------------------------------------------------------------------

        static ulong U64(JsonValue v) => v.AsU64();

        void Eq<T>(ConformanceReport r, string label, T expected, T actual)
        {
            r.Checks++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                r.Failures.Add(label + ": expected " + Show(expected) + " but got " + Show(actual));
        }

        static string Show<T>(T v) => v == null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "?";

        /// <summary>Doubles: <c>==</c> first, then within 1e-12 (reported as inexact), else a failure.</summary>
        void Dbl(ConformanceReport r, string label, double expected, double actual)
        {
            r.Checks++;
            if (expected == actual) return;
            if (Math.Abs(expected - actual) <= DoubleTolerance)
                r.Inexact.Add(label + ": " + expected.ToString("R", CultureInfo.InvariantCulture) + " vs " + actual.ToString("R", CultureInfo.InvariantCulture));
            else
                r.Failures.Add(label + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + " but got " + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        static string? NullableString(JsonValue v) => v.IsNull ? null : v.AsString();

        static string Join<T>(IEnumerable<T> items) => "[" + string.Join(", ", items.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture))) + "]";

        // -- profiles --------------------------------------------------------------------------

        /// <summary><c>Profile.Evaluate(n)</c> for n = 1..150: every field; doubles == first, then within 1e-12.</summary>
        public ConformanceReport VerifyProfiles()
        {
            var r = new ConformanceReport();
            var doc = JsonValue.Parse(_readText(new[] { "profiles", "evaluations.json" }));
            Eq(r, "evaluations count", 150, doc.Count);
            foreach (var e in doc.Items)
            {
                int n = e["number"].AsInt();
                string at = "profiles n=" + n;
                var p = _profile.Evaluate(n);
                Eq(r, at + " number", n, p.Number);
                Dbl(r, at + " difficulty", e["difficulty"].AsDouble(), p.Difficulty);
                Dbl(r, at + " target_win_rate", e["target_win_rate"].AsDouble(), p.TargetWinRate);
                Dbl(r, at + " goal_amount", e["goal_amount"].AsDouble(), p.GoalAmount);
                Dbl(r, at + " score_per_move", e["score_per_move"].AsDouble(), p.ScorePerMove);
                Dbl(r, at + " collect_per_move", e["collect_per_move"].AsDouble(), p.CollectPerMove);
                Eq(r, at + " is_intro", e["is_intro"].AsBool(), p.IsIntro);
                Eq(r, at + " is_pinch", e["is_pinch"].AsBool(), p.IsPinch);
                Eq(r, at + " is_release", e["is_release"].AsBool(), p.IsRelease);
                Eq(r, at + " intro", NullableString(e["intro"]), p.Intro);
                Eq(r, at + " colors", e["colors"].AsInt(), p.Colors);
                Eq(r, at + " board_size", e["board_size"].AsInt(), p.BoardSize);
                Eq(r, at + " move_limit", e["move_limit"].AsInt(), p.MoveLimit);
                Eq(r, at + " force_goal", NullableString(e["force_goal"]), p.ForceGoal);
                Eq(r, at + " shaped", e["shaped"].AsBool(), p.Shaped);
                Eq(r, at + " force_holes", e["force_holes"].AsBool(), p.ForceHoles);
                Eq(r, at + " specials", Join(e["specials"].Items.Select(s => s.AsString())), Join(p.Specials));
                Eq(r, at + " unlocked", Join(e["unlocked"].Items.Select(s => s.AsString())), Join(p.Unlocked));
                var pool = e["goal_pool"].Items;
                Eq(r, at + " goal_pool length", pool.Count, p.GoalPool.Count);
                for (int i = 0; i < Math.Min(pool.Count, p.GoalPool.Count); i++)
                {
                    Eq(r, at + " goal_pool[" + i + "].kind", pool[i][0].AsString(), p.GoalPool[i].Kind);
                    Dbl(r, at + " goal_pool[" + i + "].weight", pool[i][1].AsDouble(), p.GoalPool[i].Weight);
                }
            }
            return r;
        }

        // -- levels ----------------------------------------------------------------------------

        /// <summary>The file names of the level fixtures.</summary>
        public string[] LevelFiles() => _listFiles("levels").Where(f => f.StartsWith("level_", StringComparison.Ordinal) && f.EndsWith(".json", StringComparison.Ordinal)).ToArray();

        /// <summary>Generates the fixture's (n, seed) and compares the whole <see cref="Level"/> exactly.</summary>
        public ConformanceReport VerifyLevelFile(string fileName)
        {
            var r = new ConformanceReport();
            var doc = JsonValue.Parse(_readText(new[] { "levels", fileName }));
            int n = doc["n"].AsInt();
            ulong seed = U64(doc["seed"]);
            var expected = Level.FromJson(doc["level"]);
            Eq(r, fileName + " level number", n, expected.Number);
            Eq(r, fileName + " level seed", seed, expected.Seed);
            var actual = _source.Get(n, seed);
            r.Checks++;
            if (!expected.Equals(actual)) // exact, including MeasuredWinRate and the star thresholds
                foreach (var d in Diff(expected, actual)) r.Failures.Add(fileName + ": " + d);
            r.Checks++;
            if (expected.ToJson() != actual.ToJson()) r.Failures.Add(fileName + ": JSON differs: " + expected.ToJson() + " vs " + actual.ToJson());
            return r;
        }

        /// <summary>All level fixtures.</summary>
        public ConformanceReport VerifyLevels()
        {
            var r = new ConformanceReport();
            var files = LevelFiles();
            Eq(r, "level fixture count", 74, files.Length);
            foreach (var f in files) r.Merge(VerifyLevelFile(f));
            return r;
        }

        static List<string> Diff(Level e, Level a)
        {
            var d = new List<string>();
            void Cmp<T>(string name, T x, T y) { if (!EqualityComparer<T>.Default.Equals(x, y)) d.Add(name + ": expected " + Show(x) + " but got " + Show(y)); }
            Cmp("number", e.Number, a.Number);
            Cmp("seed", e.Seed, a.Seed);
            Cmp("width", e.Width, a.Width);
            Cmp("height", e.Height, a.Height);
            Cmp("holes", Join(e.Holes), Join(a.Holes));
            Cmp("colors", e.Colors, a.Colors);
            Cmp("goals", Join(e.Goals.Select(g => g.Kind + ":" + g.Target + "@" + Join(g.Colors))), Join(a.Goals.Select(g => g.Kind + ":" + g.Target + "@" + Join(g.Colors))));
            Cmp("move_limit", e.MoveLimit, a.MoveLimit);
            Cmp("star2", e.Star2, a.Star2);
            Cmp("star3", e.Star3, a.Star3);
            Cmp("specials", Join(e.Specials), Join(a.Specials));
            Cmp("intro", e.Intro, a.Intro);
            Cmp("difficulty", e.Difficulty.ToString("R", CultureInfo.InvariantCulture), a.Difficulty.ToString("R", CultureInfo.InvariantCulture));
            Cmp("target_win_rate", e.TargetWinRate.ToString("R", CultureInfo.InvariantCulture), a.TargetWinRate.ToString("R", CultureInfo.InvariantCulture));
            Cmp("measured_win_rate", e.MeasuredWinRate?.ToString("R", CultureInfo.InvariantCulture), a.MeasuredWinRate?.ToString("R", CultureInfo.InvariantCulture));
            Cmp("generator_version", e.GeneratorVersion, a.GeneratorVersion);
            if (d.Count == 0) d.Add("records differ in a way the field list does not show");
            return d;
        }

        // -- bot -------------------------------------------------------------------------------

        /// <summary><c>bot/runs.json</c>: the win flag, final score and move count of 20 bot runs on 10 levels.</summary>
        public ConformanceReport VerifyBot()
        {
            var r = new ConformanceReport();
            var runs = JsonValue.Parse(_readText(new[] { "bot", "runs.json" }));
            Eq(r, "bot run count", 20, runs.Count);
            var boards = new List<(int N, ulong Seed, Level Level, Board Board)>();
            foreach (var e in runs.Items)
            {
                int n = e["n"].AsInt();
                ulong seed = U64(e["seed"]);
                int index = boards.FindIndex(b => b.N == n && b.Seed == seed);
                if (index < 0)
                {
                    var level = _source.Get(n, seed);
                    boards.Add((n, seed, level, Bot.StartBoard(level)));
                    index = boards.Count - 1;
                }
                var (_, _, lvl, board) = boards[index];
                int run = e["run"].AsInt();
                string at = "bot n=" + n + " run=" + run;
                var actual = Bot.Play(lvl, _rules, _settings, run, board);
                Eq(r, at + " won", e["won"].AsBool(), actual.Won);
                Eq(r, at + " score", e["score"].AsInt(), actual.Score);
                Eq(r, at + " moves", e["moves"].AsInt(), actual.Moves);
            }
            return r;
        }

        // -- everything ------------------------------------------------------------------------

        /// <summary>Profiles, levels and bot runs.</summary>
        public ConformanceReport VerifyAll()
        {
            var r = VerifyProfiles();
            r.Merge(VerifyLevels());
            r.Merge(VerifyBot());
            return r;
        }

        /// <summary>The default file-system access: reads under a conformance folder.</summary>
        public static GenerationConformance ForFolder(string conformanceRoot, Profile profile, DialRegistry dials) =>
            new GenerationConformance(
                parts => File.ReadAllText(Path.Combine(new[] { conformanceRoot }.Concat(parts).ToArray()), System.Text.Encoding.UTF8),
                folder => Directory.GetFiles(Path.Combine(conformanceRoot, folder)).Select(Path.GetFileName).Select(f => f!).OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                profile, dials);
    }
}
