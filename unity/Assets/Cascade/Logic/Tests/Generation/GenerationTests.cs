using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Platform.Abstractions;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Generation
{
    /// <summary>Tests (TRD §9.1 Generation).</summary>
    public class GenerationTests
    {
        // Fast + deterministic dials for tests: small K/R, no wall-clock cap.
        static readonly DialRegistry FastDials = TestDials.Fast();
        static readonly GeneratorSettings Fast = RuleSets.GeneratorFrom(FastDials);
        static readonly RuleSet Rules = RuleSets.From(FastDials);
        static readonly Profile Prof = TestDials.Profile();

        static readonly int[] Numbers = { 1, 5, 7, 8, 11, 12, 21, 30, 36, 37, 45, 51, 60, 76, 77, 100, 149, 150 };

        public static IEnumerable<int> LevelNumbers => Numbers;

        static ulong[] Seeds(int count, ulong salt = 0)
        {
            var list = new ulong[count];
            for (int i = 0; i < count; i++) list[i] = Rng.SplitMix64((ulong)i * 7919UL + salt);
            return list;
        }

        static Level Candidate(int n, ulong seed, GeneratorSettings? settings = null) =>
            LevelGenerator.MakeCandidate(Prof.Evaluate(n), seed, settings ?? Fast);

        static GeneratingLevelSource Source(DialRegistry? dials = null, IStopwatch? stopwatch = null, Profile? profile = null, bool hotReload = false) =>
            new GeneratingLevelSource(dials ?? FastDials, profile ?? Prof, stopwatch ?? new FakeStopwatch(), hotReload);

        // -- candidate properties (324 level/seed pairs, no bot) ---------------------------------

        [TestCaseSource(nameof(LevelNumbers))]
        public void Candidates_are_valid_and_respect_unlocks(int n)
        {
            var p = Prof.Evaluate(n);
            foreach (ulong seed in Seeds(18, (ulong)n))
            {
                var lvl = Candidate(n, seed);
                Assert.That(Validator.Validate(lvl), Is.Empty);
                var board = lvl.BuildBoard(new Streams(lvl.Seed));
                Assert.That(Match.FindRuns(board), Is.Empty, "no pre-made matches"); // the stated invariants
                Assert.That(Moves.FindLegal(board), Is.Not.Empty, "at least one legal move");
                Assert.That(lvl.GeneratorVersion, Is.EqualTo(GeneratorVersion.Current));
                Assert.That(lvl.Number, Is.EqualTo(n));
                Assert.That(lvl.Colors, Is.EqualTo(p.Colors));
                Assert.That(lvl.Specials, Is.EqualTo(p.Specials));
                Assert.That(lvl.Intro, Is.EqualTo(p.Intro));
                Assert.That(lvl.Width, Is.EqualTo(p.BoardSize));
                Assert.That(lvl.Height, Is.EqualTo(p.BoardSize));
                var kinds = lvl.Goals.Select(g => g.Kind).ToList();
                Assert.That(!kinds.Contains(GoalKind.Collect) || n >= 11);
                Assert.That(lvl.Goals.Count != 2 || n >= 76);
                Assert.That(lvl.Holes.Count == 0 || n >= 36);
                var present = new HashSet<int>(board.Topology.Cells.Select(c => board.TileAt(c)!.Color));
                foreach (var g in lvl.Goals)
                {
                    Assert.That(g.Target, Is.GreaterThanOrEqualTo(1));
                    if (g.Kind != GoalKind.Collect) continue;
                    Assert.That(g.Colors.All(c => c >= 0 && c < lvl.Colors));
                    Assert.That(g.Colors.All(present.Contains), "goal colors are on the board (reachable)");
                }
            }
        }

        [Test]
        public void Intro_levels_feature_their_mechanic()
        {
            foreach (ulong seed in Seeds(10, 3))
            {
                Assert.That(Candidate(11, seed).Goals.Select(g => g.Kind), Does.Contain(GoalKind.Collect));
                Assert.That(Candidate(36, seed).Holes.Count, Is.GreaterThan(0));
                Assert.That(Candidate(76, seed).Goals.Select(g => g.Kind).ToArray(), Is.EqualTo(new[] { GoalKind.Score, GoalKind.Collect }));
            }
            Assert.That(Candidate(11, 1).Intro, Is.EqualTo("Collect goals and Line pieces"));
        }

        [Test]
        public void Holes_are_symmetric_connected_and_leave_enough_cells()
        {
            var seen = new HashSet<string>();
            foreach (int size in new[] { 7, 8, 9 })
            {
                foreach (var option in LevelGenerator.HoleOptions(size))
                {
                    var hs = new HashSet<(int, int)>(option.Holes.Select(h => (h.X, h.Y)));
                    Assert.That(hs.SetEquals(hs.Select(h => (size - 1 - h.Item1, h.Item2))), option.Name);
                    Assert.That(hs.SetEquals(hs.Select(h => (h.Item1, size - 1 - h.Item2))), option.Name);
                    Assert.That(hs.SetEquals(hs.Select(h => (h.Item2, h.Item1))), option.Name); // also diagonal-symmetric
                    Assert.That(option.Holes, Is.EqualTo(option.Holes.OrderBy(h => h.X).ThenBy(h => h.Y).ToArray()), "sorted by (x, y)");
                    if (size * size - hs.Count >= Fast.MinCells)
                    {
                        Assert.That(LevelGenerator.IsConnected(size, size, option.Holes), $"{size} {option.Name}");
                        seen.Add(option.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'));
                    }
                }
            }
            Assert.That(seen.OrderBy(s => s, StringComparer.Ordinal).ToArray(), Is.EqualTo(new[] { "corners", "cross", "diamond", "ring" }));
            var shaped = new HashSet<string>(Seeds(60, 9).Select(s => string.Join(";", Candidate(40, s).Holes)));
            Assert.That(shaped, Does.Contain("")); // holes_chance < 1
            Assert.That(shaped.Count, Is.GreaterThan(3)); // several templates in play
        }

        [Test]
        public void Disconnected_or_empty_holes_are_not_connected()
        {
            Assert.That(LevelGenerator.IsConnected(5, 5, Enumerable.Range(0, 5).Select(y => (2, y))), Is.False);
            Assert.That(LevelGenerator.IsConnected(2, 2, new[] { (0, 0), (1, 0), (0, 1), (1, 1) }), Is.False);
            Assert.That(LevelGenerator.IsConnected(3, 3, new (int, int)[0]));
        }

        [Test]
        public void Generator_streams_make_levels_deterministic()
        {
            foreach (int n in new[] { 3, 36, 76 })
                foreach (ulong seed in Seeds(5, (ulong)n))
                    Assert.That(Candidate(n, seed), Is.EqualTo(Candidate(n, seed)));
            Assert.That(Candidate(40, 1), Is.Not.EqualTo(Candidate(40, 2)));
        }

        // -- the full pipeline -----------------------------------------------------------------

        [Test]
        public void Full_generation_is_deterministic_without_a_budget()
        {
            foreach (var (n, seed) in new[] { (1, 11UL), (11, 22UL), (36, 33UL), (76, 44UL) })
            {
                var a = Source().Get(n, seed);
                var b = Source().Get(n, seed);
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a.Seed, Is.EqualTo(seed));
                Assert.That(a.ToJson(), Is.EqualTo(b.ToJson()));
            }
        }

        [Test]
        public void Property_run_over_pairs_generated_levels_are_valid_and_deterministic()
        {
            int pairs = 0;
            foreach (int n in new[] { 2, 9, 11, 22, 36, 41, 52, 77, 120 })
                foreach (ulong seed in Seeds(7, (ulong)n * 31))
                {
                    var a = Source().Get(n, seed);
                    Assert.That(Validator.Validate(a), Is.Empty, $"{n}/{seed}");
                    Assert.That(Source().Get(n, seed), Is.EqualTo(a), $"{n}/{seed}");
                    pairs++;
                }
            Assert.That(pairs, Is.GreaterThanOrEqualTo(60));
        }

        [Test]
        public void Generated_levels_are_tuned_and_playable()
        {
            var src = Source();
            Assert.That(src, Is.InstanceOf<ILevelSource>());
            Assert.That(src.LastStats, Is.Null);
            foreach (int n in new[] { 1, 8, 14, 36, 80 })
            {
                var lvl = src.Get(n, 5);
                Assert.That(Validator.Validate(lvl), Is.Empty);
                Assert.That(lvl.MeasuredWinRate, Is.Not.Null);
                Assert.That(lvl.MeasuredWinRate!.Value, Is.InRange(0.0, 1.0));
                Assert.That(lvl.Star2, Is.GreaterThan(0));
                Assert.That(lvl.Star3, Is.GreaterThan(lvl.Star2));
                Assert.That(lvl.MoveLimit, Is.InRange(Fast.MoveLimitMin, Fast.MoveLimitMax));
                Assert.That(src.LastStats!.Rounds, Is.GreaterThanOrEqualTo(1));
                Assert.That(src.LastStats!.BotRuns, Is.GreaterThanOrEqualTo(6));
                Assert.That(src.LastStats!.BotMoves, Is.GreaterThan(0));
                Assert.That(Level.FromJson(lvl.ToJson()), Is.EqualTo(lvl));
            }
        }

        [Test]
        public void A_retry_gets_the_identical_level_from_the_cache()
        {
            var src = Source();
            var a = src.Get(2, 100);
            var stats = src.LastStats;
            var b = src.Get(2, 101);
            Assert.That(b.Seed, Is.Not.EqualTo(a.Seed));
            Assert.That(src.Get(2, 100), Is.SameAs(a)); // a retry gets the identical level
            Assert.That(src.LastStats, Is.Not.SameAs(stats), "LastStats describes the last real generation");
            Assert.That(src.Get(2, 101), Is.SameAs(b));
        }

        [Test]
        public void Cache_keeps_the_last_eight_levels_by_number_and_seed()
        {
            var src = Source();
            var first = src.Get(1, 1);
            var second = src.Get(1, 2);
            for (ulong seed = 3; seed <= 9; seed++) src.Get(1, seed); // 7 more: 9 distinct levels in total
            Assert.That(src.Get(1, 2), Is.SameAs(second), "within the last 8");
            Assert.That(src.Get(1, 1), Is.Not.SameAs(first), "the oldest was evicted");
            Assert.That(src.Get(1, 1), Is.EqualTo(first), "but regenerates identically");
            Assert.That(src.Get(2, 2), Is.Not.SameAs(second), "keyed by number as well as seed");
        }

        [Test]
        public void Budget_caps_work_but_keeps_min_runs()
        {
            var tight = TestDials.Load(d =>
            {
                d.Set("generator", "budget_ms", 1);
                d.Set("generator", "bot_runs", 20);
                d.Set("generator", "tune_rounds", 3);
            });
            // every clock read advances 10 ms, so the 1 ms budget is gone as soon as the clock is looked at
            var src = Source(tight, new FakeStopwatch { AutoAdvanceMs = 10 });
            // the budget is only checked when the first measurement misses the target, so take the first seed that does
            Level lvl = null!;
            for (ulong seed = 99; seed < 140; seed++)
            {
                lvl = src.Get(30, seed);
                if (!src.LastStats!.WithinTolerance) break;
            }
            Assert.That(Validator.Validate(lvl), Is.Empty);
            Assert.That(src.LastStats!.OverBudget);
            Assert.That(src.LastStats!.Rounds, Is.EqualTo(1));
            Assert.That(src.LastStats!.BotRuns, Is.EqualTo(5)); // min_bot_runs
            Assert.That(src.LastStats!.Ms, Is.GreaterThan(0));
        }

        [Test]
        public void A_zero_budget_never_reads_the_clock_for_decisions()
        {
            var clock = new FakeStopwatch { AutoAdvanceMs = 1_000_000 };
            var lvl = Source(FastDials, clock).Get(30, 99);
            Assert.That(lvl, Is.EqualTo(Source(FastDials, new FakeStopwatch()).Get(30, 99)));
        }

        [Test]
        public void Hot_reload_regenerates_after_a_profile_change()
        {
            var storage = new MemoryStorage();
            var data = new DictionaryDataSource().With(Profile.DataKey, TestDials.ProfileText());
            var prof = Profile.Load(data, storage);
            var src = Source(profile: prof, hotReload: true);
            var first = src.Get(3, 1);
            Assert.That(src.Get(3, 1), Is.SameAs(first));
            var text = TestDials.ProfileText().Replace("hardest_win_rate = 0.35", "hardest_win_rate = 0.30");
            Assert.That(text, Is.Not.EqualTo(TestDials.ProfileText()));
            storage.PutText(Profile.DevOverrideFile, text);
            var second = src.Get(3, 1);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.TargetWinRate, Is.Not.EqualTo(first.TargetWinRate));
        }

        [Test]
        public void Without_hot_reload_a_profile_file_change_is_ignored()
        {
            var storage = new MemoryStorage();
            var data = new DictionaryDataSource().With(Profile.DataKey, TestDials.ProfileText());
            var src = Source(profile: Profile.Load(data, storage), hotReload: false);
            var first = src.Get(3, 1);
            storage.PutText(Profile.DevOverrideFile, TestDials.ProfileText().Replace("hardest_win_rate = 0.35", "hardest_win_rate = 0.30"));
            Assert.That(src.Get(3, 1), Is.SameAs(first));
        }

        [Test]
        public void A_dial_change_drops_the_cache_and_changes_the_level()
        {
            var dials = TestDials.Fast();
            var src = Source(dials);
            var first = src.Get(5, 1);
            Assert.That(src.Get(5, 1), Is.SameAs(first));
            dials.Set("generator", "goal_score_round", 5);
            dials.Set("scoring", "run3", 90);
            var second = src.Get(5, 1);
            Assert.That(second, Is.Not.SameAs(first));
        }

        // -- validator -------------------------------------------------------------------------

        [Test]
        public void Validator_flags_bad_levels()
        {
            var good = Candidate(11, 4);
            Assert.That(Validator.IsValid(good));
            Assert.That(Validator.Validate(good with { MoveLimit = 0 }), Is.Not.Empty);
            Assert.That(Validator.Validate(good with { Goals = new Goal[0] }), Is.Not.Empty);
            Assert.That(Validator.Validate(good with { Star2 = 10, Star3 = 5 }), Is.Not.Empty);
            Assert.That(Validator.Validate(good with { Specials = new[] { "Teleport" } }), Is.Not.Empty);
            Assert.That(Validator.Validate(good with { Colors = 2 }), Is.Not.Empty);
            Assert.That(Validator.Validate(good with { Colors = 9 }), Is.Not.Empty);
            var colorless = good with { Goals = new[] { Goal.Collect(good.Goals[0].Target, good.Colors) } };
            Assert.That(Validator.Validate(colorless).Any(p => p.Contains("color")));
            // a hole pattern that splits the board in two
            var split = good with { Width = 5, Height = 5, Holes = Enumerable.Range(0, 5).Select(y => (2, y)).ToArray() };
            Assert.That(Validator.Validate(split).Any(p => p.Contains("connected")));
            var tiny = good with { Width = 2, Height = 2 };
            Assert.That(Validator.Validate(tiny).Any(p => p.Contains("too few") || p.Contains("cannot build")));
        }

        // -- bot -------------------------------------------------------------------------------

        [Test]
        public void Bot_runs_differ_but_share_the_initial_board()
        {
            var lvl = Candidate(21, 77);
            var baseBoard = Bot.StartBoard(lvl);
            var s0 = new Session(lvl, Rules, null, Bot.RunStreams(lvl.Seed, 0), baseBoard.Copy());
            var s1 = new Session(lvl, Rules, null, Bot.RunStreams(lvl.Seed, 1), baseBoard.Copy());
            Assert.That(baseBoard.Topology.Cells.Select(c => s0.Board.TileAt(c)),
                Is.EqualTo(baseBoard.Topology.Cells.Select(c => s1.Board.TileAt(c))));
            var results = new HashSet<BotRun>(Enumerable.Range(0, 8).Select(i => Bot.Play(lvl, Rules, Fast, i, baseBoard)));
            Assert.That(results.Count, Is.GreaterThan(1)); // random moves + different play streams
            Assert.That(Bot.Play(lvl, Rules, Fast, 3, baseBoard), Is.EqualTo(Bot.Play(lvl, Rules, Fast, 3))); // deterministic per run index
        }

        [Test]
        public void Bot_does_not_change_the_base_board_it_is_given()
        {
            var lvl = Candidate(21, 77);
            var baseBoard = Bot.StartBoard(lvl);
            var before = baseBoard.Topology.Cells.Select(c => baseBoard.TileAt(c)).ToList();
            Bot.Play(lvl, Rules, Fast, 0, baseBoard);
            Assert.That(baseBoard.Topology.Cells.Select(c => baseBoard.TileAt(c)), Is.EqualTo(before));
        }

        [Test]
        public void Bot_drives_the_real_session_to_an_end()
        {
            var lvl = Candidate(5, 1);
            var streams = Bot.RunStreams(lvl.Seed, 0);
            var ses = new Session(lvl, Rules, Fast.Weights, streams, Bot.StartBoard(lvl));
            var rng = streams.Bot;
            while (ses.Status == SessionStatus.Playing)
            {
                var (a, b) = rng.Choice(ses.BestMoves());
                ses.TrySwap(a, b);
            }
            var run = Bot.Play(lvl, Rules, Fast, 0);
            Assert.That(ses.MovesUsed, Is.LessThanOrEqualTo(lvl.MoveLimit));
            Assert.That(ses.Score, Is.GreaterThan(0));
            Assert.That(run.Score, Is.GreaterThan(0));
            Assert.That(run.Moves, Is.InRange(1, lvl.MoveLimit));
        }

        [Test]
        public void Bot_wins_an_easy_level_and_loses_an_impossible_one()
        {
            var lvl = Candidate(2, 8);
            var easy = lvl with { Goals = new[] { Goal.Score(200) } };
            var hard = lvl with { Goals = new[] { Goal.Score(10_000_000) }, MoveLimit = 5 };
            Assert.That(Bot.Measure(easy, Rules, Fast, 5).WinRate, Is.EqualTo(1.0));
            Assert.That(Bot.Measure(hard, Rules, Fast, 5).WinRate, Is.EqualTo(0.0));
        }

        [Test]
        public void Measure_honours_the_deadline_but_not_below_the_minimum()
        {
            var lvl = Candidate(5, 3);
            var clock = new FakeStopwatch { AutoAdvanceMs = 10 };
            clock.Advance(100); // already past the 1 ms deadline at the first look
            var m = Bot.Measure(lvl, Rules, Fast, 20, null, 1.0, clock);
            Assert.That(m.Runs.Count, Is.EqualTo(Fast.MinBotRuns));
            Assert.That(Bot.Measure(lvl, Rules, Fast, 20).Runs.Count, Is.EqualTo(20));
            Assert.That(Bot.Measure(lvl, Rules, Fast, 3, null, 1.0, clock).Runs.Count, Is.EqualTo(3)); // runs < minimum
            var later = Bot.Measure(lvl, Rules, Fast, 4, null, null, null, firstRun: 4);
            Assert.That(later.Runs[0], Is.EqualTo(Bot.Play(lvl, Rules, Fast, 4)));
        }

        [Test]
        public void Measurement_statistics()
        {
            var m = new Measurement(new[] { new BotRun(true, 300, 10), new BotRun(false, 100, 20), new BotRun(true, 200, 12) });
            Assert.That(m.WinRate, Is.EqualTo(2.0 / 3.0));
            Assert.That(m.WinScores, Is.EqualTo(new[] { 200, 300 }));
            Assert.That(m.AllScores, Is.EqualTo(new[] { 100, 200, 300 }));
            Assert.That(m.Moves, Is.EqualTo(42));
            Assert.That(new Measurement().WinRate, Is.EqualTo(0.0));
        }

        // -- tuner -----------------------------------------------------------------------------

        [Test]
        public void Percentile_and_star_thresholds()
        {
            Assert.That(Tuner.Percentile(new[] { 10, 20, 30, 40, 50 }, 50), Is.EqualTo(30));
            Assert.That(Tuner.Percentile(new[] { 10, 20 }, 25), Is.EqualTo(12.5));
            Assert.That(Tuner.Percentile(new[] { 7 }, 85), Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => Tuner.Percentile(new int[0], 50));
            var m = new Measurement(new[] { 100, 200, 300, 400, 500 }.Select(s => new BotRun(true, s, 10)));
            Assert.That(Tuner.StarThresholds(m, Fast), Is.EqualTo((300, 440))); // 50th and 85th percentile
            var few = new Measurement(new[] { 100, 200, 300 }.Select(s => new BotRun(false, s, 10)));
            var (two, three) = Tuner.StarThresholds(few, Fast); // fallback: multiples of the median score
            Assert.That(two, Is.EqualTo(MathX.RoundHalfEven(200 * Fast.Stars.FallbackTwo)));
            Assert.That(three, Is.EqualTo(MathX.RoundHalfEven(200 * Fast.Stars.FallbackThree)));
            var (e2, e3) = Tuner.StarThresholds(new Measurement(), Fast);
            Assert.That(e2, Is.GreaterThan(0));
            Assert.That(e2, Is.LessThan(e3));
        }

        [Test]
        public void Adjust_moves_first_then_goal_size()
        {
            var lvl = Candidate(5, 3);
            var harder = Tuner.Adjust(lvl, 1.0, Fast); // too easy -> fewer moves, by 1..3
            Assert.That(lvl.MoveLimit - harder.MoveLimit, Is.InRange(1, 3));
            var easier = Tuner.Adjust(lvl, 0.0, Fast);
            Assert.That(easier.MoveLimit - lvl.MoveLimit, Is.InRange(1, 3));
            var atMin = lvl with { MoveLimit = Fast.MoveLimitMin };
            var biggerGoal = Tuner.Adjust(atMin, 1.0, Fast);
            Assert.That(biggerGoal.MoveLimit, Is.EqualTo(Fast.MoveLimitMin));
            Assert.That(biggerGoal.Goals[0].Target, Is.GreaterThan(atMin.Goals[0].Target));
            var atMax = lvl with { MoveLimit = Fast.MoveLimitMax };
            Assert.That(Tuner.Adjust(atMax, 0.0, Fast).Goals[0].Target, Is.LessThan(atMax.Goals[0].Target));
        }

        [Test]
        public void Adjust_clamps_to_the_bound_before_touching_the_goals()
        {
            var lvl = Candidate(5, 3) with { MoveLimit = Fast.MoveLimitMin + 1 };
            var adjusted = Tuner.Adjust(lvl, 1.0, Fast); // would go below the minimum by 2
            Assert.That(adjusted.MoveLimit, Is.EqualTo(Fast.MoveLimitMin));
            Assert.That(adjusted.Goals, Is.EqualTo(lvl.Goals));
        }

        [Test]
        public void Scale_goals_keeps_kind_colors_and_rounding()
        {
            var goals = new[] { Goal.Score(1000), Goal.Collect(20, 1, 3) };
            var scaled = LevelGenerator.ScaleGoals(goals, 1.37, Fast);
            Assert.That(scaled[0], Is.EqualTo(Goal.Score(1350))); // multiple of goal_score_round
            Assert.That(scaled[1], Is.EqualTo(Goal.Collect(27, 1, 3)));
            Assert.That(LevelGenerator.ScaleGoals(new[] { Goal.Score(60) }, 0.01, Fast)[0].Target, Is.EqualTo(Fast.GoalScoreRound));
            Assert.That(LevelGenerator.ScaleGoals(new[] { Goal.Collect(2, 0) }, 0.1, Fast)[0].Target, Is.EqualTo(1));
        }

        [Test]
        public void Tuner_keeps_the_closest_candidate()
        {
            var lvl = Candidate(30, 12);
            var (tuned, stats) = Tuner.Tune(lvl, Rules, Fast);
            Assert.That(tuned.MeasuredWinRate, Is.Not.Null);
            Assert.That(stats.Rounds, Is.LessThanOrEqualTo(Fast.TuneRounds + 1));
            if (stats.WithinTolerance)
                Assert.That(Math.Abs(tuned.MeasuredWinRate!.Value - tuned.TargetWinRate), Is.LessThanOrEqualTo(Fast.WinRateTolerance));
            Assert.That(tuned.Seed, Is.EqualTo(lvl.Seed));
        }

        [Test]
        public void Measured_win_rate_is_reproduced_by_re_running_the_bot_on_the_stored_level()
        {
            var src = Source(TestDials.NoBudget());
            var lvl = src.Get(14, 4242);
            var m = Bot.Measure(lvl, RuleSets.From(TestDials.NoBudget()), RuleSets.GeneratorFrom(TestDials.NoBudget()), 20);
            Assert.That(m.WinRate, Is.EqualTo(lvl.MeasuredWinRate!.Value));
        }
    }
}
