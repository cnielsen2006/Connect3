using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Presentation.Shell;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 12.3 / 8, PRD 2.7 / 3.1: the play-session lifecycle, generation queue, seeds and result flow.</summary>
    public class GameControllerTests
    {
        static SaveData Frontier(int level, ulong? seed = null, params (int Level, int Stars)[] stars) =>
            new SaveData
            {
                CurrentLevel = level,
                CurrentSeed = seed,
                Stars = StarTable.From(stars.Select(s => new KeyValuePair<int, int>(s.Level, s.Stars))),
            };

        // ---- preparing a level

        [Test]
        public void Selecting_a_level_shows_Preparing_until_the_job_finishes_then_the_intro_with_a_session()
        {
            var fx = new ShellFixture();
            fx.Flow.Play();
            Assert.That(fx.Flow.SelectLevel(1));

            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
            Assert.That(fx.Game.PreparingLevel, Is.EqualTo(1));
            Assert.That(fx.Game.PreparingText, Is.EqualTo("Preparing level 1"));
            Assert.That(fx.Game.Session, Is.Null);
            Assert.That(fx.Runner.Pending, Is.EqualTo(1), "generation was handed to the job runner, not run inline");

            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(fx.Game.PreparingLevel, Is.Null);
            Assert.That(fx.Game.PreparingText, Is.EqualTo(""));
            Assert.That(fx.Game.Level!.Number, Is.EqualTo(1));
            Assert.That(fx.Game.Session!.Level, Is.SameAs(fx.Game.Level));
            Assert.That(fx.Game.Session.MovesLeft, Is.EqualTo(40));
        }

        [Test]
        public void An_immediate_runner_makes_the_whole_flow_synchronous()
        {
            var fx = new ShellFixture(immediate: true);
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(fx.Flow.StartLevel());
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(35))!.Outcome, Is.EqualTo(MoveOutcome.Rejected));
        }

        [Test]
        public void LevelReady_fires_before_the_flow_moves_to_the_intro()
        {
            var fx = new ShellFixture();
            var order = new List<string>();
            fx.Game.LevelReady += () => order.Add("controller:" + fx.Flow.Current);
            fx.Flow.ScreenChanged += (a, b) => order.Add("flow:" + b);
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            fx.Runner.RunAll();
            Assert.That(order, Is.EqualTo(new[] { "flow:Map", "flow:Loading", "controller:Loading", "flow:Intro" }));
        }

        [Test]
        public void Backing_out_of_Loading_abandons_the_request_and_a_late_result_is_ignored()
        {
            var fx = new ShellFixture();
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            Assert.That(fx.Flow.BackPressed());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(fx.Game.PreparingLevel, Is.Null);
            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map), "the finished job does not drag the player into an intro");
            Assert.That(fx.Game.Level, Is.Null);
            Assert.That(fx.Game.Session, Is.Null);
        }

        [Test]
        public void A_failed_generation_returns_to_the_map_with_an_error_and_the_next_try_works()
        {
            var fx = new ShellFixture();
            string? message = null;
            fx.Game.LoadFailed += m => message = m;
            fx.Source.Fail = new InvalidOperationException("no level for you");
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(fx.Game.LastError, Is.EqualTo("no level for you"));
            Assert.That(message, Is.EqualTo("no level for you"));
            Assert.That(fx.Game.PreparingLevel, Is.Null);
            Assert.That(fx.Log.Lines.Single(), Does.StartWith("Error: level 1"));

            fx.Source.Fail = null;
            fx.Flow.SelectLevel(1);
            Assert.That(fx.Game.LastError, Is.Null);
            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
        }

        [Test]
        public void The_session_is_built_with_the_dials_rules_and_follows_live_dial_edits()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            Assert.That(fx.Game.Session!.Rules.Scoring.Run3, Is.EqualTo(60));
            fx.Dials.Set("scoring", "run3", 90);
            Assert.That(fx.Game.Session.Rules.Scoring.Run3, Is.EqualTo(90));
            fx.Dials.Set("generator", "bot_weight_goal", 20.0);
            Assert.That(fx.Game.Session.Weights.Goal, Is.EqualTo(20.0));
        }

        // ---- seeds

        [Test]
        public void The_frontier_level_stores_its_seed_and_a_retry_reuses_it()
        {
            var fx = new ShellFixture(save: Frontier(3));
            fx.Source.HardFrom = 3;
            fx.StartLevel(3);
            Assert.That(fx.Source.Calls, Is.EqualTo(new[] { (3, 1000UL) }));
            Assert.That(fx.Progress.Data.CurrentSeed, Is.EqualTo(1000UL));
            Assert.That(fx.Sink.Progress.Last().CurrentSeed, Is.EqualTo(1000UL), "saved as soon as it was drawn");

            fx.PlayToEnd();
            Assert.That(fx.Game.Session!.Status, Is.EqualTo(SessionStatus.Lost));
            fx.Game.ShowResult();
            Assert.That(fx.Flow.Retry());
            fx.Runner.RunAll();
            Assert.That(fx.Source.Calls.Last(), Is.EqualTo((3, 1000UL)), "retry = the same (level, seed)");
            Assert.That(fx.Seeds.NextSeed(), Is.EqualTo(1001UL), "no extra seed was drawn for the retry");
            Assert.That(fx.Game.Session!.MovesLeft, Is.EqualTo(2), "a fresh session of the same level");
        }

        [Test]
        public void An_earlier_level_gets_a_fresh_unsaved_seed()
        {
            var fx = new ShellFixture(save: Frontier(5, 777, (2, 1)));
            fx.StartLevel(2);
            Assert.That(fx.Source.Calls.Single(), Is.EqualTo((2, 1000UL)));
            Assert.That(fx.Progress.Data.CurrentSeed, Is.EqualTo(777UL));
            Assert.That(fx.Sink.Progress, Is.Empty);
        }

        [Test]
        public void A_dev_jump_uses_a_fresh_seed_and_saves_nothing()
        {
            var fx = new ShellFixture(save: Frontier(2, 555));
            fx.Flow.Play();
            fx.Flow.OpenDev();
            Assert.That(fx.Flow.DevJump(2), "even the frontier level gets a fresh seed from a jump");
            fx.Runner.RunAll();
            Assert.That(fx.Source.Calls.Single(), Is.EqualTo((2, 1000UL)));
            Assert.That(fx.Progress.Data.CurrentSeed, Is.EqualTo(555UL));
            Assert.That(fx.Sink.Progress, Is.Empty);
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
        }

        // ---- playing

        [Test]
        public void A_swap_is_refused_until_the_Play_page_is_up_and_while_paused_or_the_dev_overlay_is_open()
        {
            var fx = new ShellFixture();
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(1)), Is.Null, "no session");
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            fx.Runner.RunAll();
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(1)), Is.Null, "intro, not play");
            fx.Flow.StartLevel();
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(35)), Is.Not.Null);

            fx.Flow.Pause();
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(35)), Is.Null);
            Assert.That(fx.Game.Hint(), Is.Null);
            fx.Flow.Resume();
            fx.Flow.OpenDev();
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(35)), Is.Null);
            fx.Flow.CloseDev();
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(35)), Is.Not.Null);
            Assert.That(fx.Game.Hint(), Is.Not.Null);
        }

        [Test]
        public void A_rejected_swap_costs_nothing_and_raises_no_MovePlayed()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            int played = 0;
            fx.Game.MovePlayed += _ => played++;
            var r = fx.Game.TrySwap(new CellId(0), new CellId(35))!;
            Assert.That(r.Outcome, Is.EqualTo(MoveOutcome.Rejected));
            Assert.That(r.Events.Single(), Is.TypeOf<SwapRejected>());
            Assert.That(played, Is.EqualTo(0));
            Assert.That(fx.Game.Session!.MovesLeft, Is.EqualTo(40));
        }

        // ---- ending a level

        [Test]
        public void A_win_is_recorded_at_once_but_the_result_page_waits_for_ShowResult()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            var events = new List<string>();
            fx.Game.MovePlayed += r => events.Add("moved:" + r.Status);
            fx.Game.LevelEnded += r => events.Add("ended:" + (r.Won ? "won" : "lost"));

            fx.PlayToEnd();

            Assert.That(events, Is.EqualTo(new[] { "moved:Won", "ended:won" }));
            Assert.That(fx.Game.Session!.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(fx.Progress.CurrentLevel, Is.EqualTo(2), "the frontier advanced");
            Assert.That(fx.Progress.StarsFor(1), Is.EqualTo(2));
            Assert.That(fx.Sink.Progress.Last().CurrentLevel, Is.EqualTo(2), "and was saved before any animation finished");
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Play), "the view is still animating");
            Assert.That(fx.Game.ResultPending);
            Assert.That(fx.Game.TrySwap(new CellId(0), new CellId(1)), Is.Null, "no moves while the result is pending");

            fx.Game.ShowResult();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Result));
            Assert.That(fx.Flow.Won);
            Assert.That(fx.Game.ResultPending, Is.False);
            var result = fx.Game.LastResult!;
            Assert.That((result.Number, result.Won, result.Stars, result.PreviousBestStars, result.NewBest), Is.EqualTo((1, true, 2, 0, true)));
            Assert.That(result.Score, Is.GreaterThanOrEqualTo(60));
            fx.Game.ShowResult(); // a second call is harmless
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Result));
        }

        [Test]
        public void A_loss_records_nothing_and_offers_a_retry()
        {
            var fx = new ShellFixture(save: Frontier(4));
            fx.Source.HardFrom = 1;
            fx.StartLevel(4);
            int writesBefore = fx.Sink.Progress.Count;
            fx.PlayToEnd();
            Assert.That(fx.Game.Session!.Status, Is.EqualTo(SessionStatus.Lost));
            Assert.That(fx.Progress.CurrentLevel, Is.EqualTo(4));
            Assert.That(fx.Progress.TotalStars, Is.EqualTo(0));
            Assert.That(fx.Sink.Progress.Count, Is.EqualTo(writesBefore));
            fx.Game.ShowResult();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Result));
            Assert.That(fx.Flow.Won, Is.False);
            Assert.That(fx.Game.LastResult!.Stars, Is.EqualTo(0));
            Assert.That(fx.Game.LastResult.Goals.Count, Is.EqualTo(2));
            Assert.That(fx.Runner.Pending, Is.EqualTo(0), "nothing to pre-generate after a loss");
        }

        [Test]
        public void Winning_an_earlier_level_keeps_the_best_stars_does_not_advance_and_does_not_prefetch()
        {
            var fx = new ShellFixture(save: Frontier(5, 777, (2, 3)));
            fx.StartLevel(2);
            fx.PlayToEnd();
            fx.Game.ShowResult();
            Assert.That(fx.Progress.CurrentLevel, Is.EqualTo(5));
            Assert.That(fx.Progress.StarsFor(2), Is.EqualTo(3), "a 2 star replay never lowers the best");
            Assert.That(fx.Game.LastResult!.NewBest, Is.False);
            Assert.That(fx.Runner.Pending, Is.EqualTo(0));
        }

        [Test]
        public void ShowResult_after_the_player_left_the_Play_page_is_harmless()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            fx.PlayToEnd();
            fx.Flow.Pause();
            fx.Flow.ToMap(); // "Quit to map" while the end animation played
            fx.Game.ShowResult();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(fx.Runner.Pending, Is.EqualTo(1), "the next level is still pre-generated");
        }

        // ---- pre-generation and the queue

        [Test]
        public void After_a_win_the_next_level_is_pregenerated_and_Next_starts_without_regenerating()
        {
            var fx = new ShellFixture(makeSource: d =>
            {
                d.Set("generator", "budget_ms", 0);
                d.Set("generator", "bot_runs", 6);
                d.Set("generator", "tune_rounds", 1);
                return new GeneratingLevelSource(d, TestDials.Profile(), new FakeStopwatch());
            });
            var real = (GeneratingLevelSource)fx.LevelSource;
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            fx.Runner.RunAll();
            fx.Flow.StartLevel();
            fx.PlayToEnd(maxMoves: 400);
            if (fx.Game.Session!.Status != SessionStatus.Won) Assert.Inconclusive("the generated level 1 was not won by hints");
            fx.Game.ShowResult();

            ulong nextSeed = fx.Progress.Data.CurrentSeed!.Value;
            Assert.That(fx.Runner.Pending, Is.EqualTo(1), "pre-generation of level 2 was started");
            Assert.That(fx.Game.NextLevelReady, Is.False);
            Assert.That(fx.Game.PreparingLevel, Is.Null, "the screen is the result, not 'Preparing'");
            fx.Runner.RunAll();
            Assert.That(fx.Game.NextLevelReady);
            Assert.That(fx.Game.IsPrepared(2, nextSeed));
            var stats = real.LastStats;

            Assert.That(fx.Flow.Next());
            Assert.That(fx.Game.PreparingText, Is.EqualTo("Preparing level 2"));
            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(fx.Game.Level!.Number, Is.EqualTo(2));
            Assert.That(fx.Game.Level.Seed, Is.EqualTo(nextSeed), "the seed RecordWin drew");
            Assert.That(real.LastStats, Is.SameAs(stats), "served from the (number, seed) cache, not generated again");
            Assert.That(real.Get(2, nextSeed), Is.SameAs(fx.Game.Level));
        }

        [Test]
        public void Pressing_Next_while_the_prefetch_runs_joins_that_job_instead_of_starting_a_second()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            fx.PlayToEnd();
            fx.Game.ShowResult();
            Assert.That(fx.Runner.Started, Is.EqualTo(2), "level 1, then the prefetch");
            Assert.That(fx.Game.NextLevelReady, Is.False);

            Assert.That(fx.Flow.Next());
            Assert.That(fx.Runner.Started, Is.EqualTo(2), "no second job for the same (level, seed)");
            Assert.That(fx.Runner.Pending, Is.EqualTo(1));
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
            Assert.That(fx.Game.PreparingText, Is.EqualTo("Preparing level 2"));
            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(fx.Source.Calls.Count(c => c.Number == 2), Is.EqualTo(1));
        }

        [Test]
        public void Jobs_never_overlap_and_the_level_the_player_waits_for_goes_before_a_prefetch()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            fx.PlayToEnd();
            fx.Game.ShowResult(); // starts the prefetch of level 2 (running, not finished)
            Assert.That(fx.Flow.ToMap());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(fx.Runner.Pending, Is.EqualTo(1));
            int started = fx.Runner.Started;

            Assert.That(fx.Flow.SelectLevel(1), "replay level 1 while the prefetch is still running");
            Assert.That(fx.Runner.Started, Is.EqualTo(started), "the new job waits: only one at a time");
            Assert.That(fx.Runner.Pending, Is.EqualTo(1));

            fx.Runner.RunNext(); // the prefetch finishes, the waiting job starts
            Assert.That(fx.Runner.Started, Is.EqualTo(started + 1));
            Assert.That(fx.Runner.Pending, Is.EqualTo(1));
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
            fx.Runner.RunNext();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(fx.Source.Calls.Select(c => c.Number), Is.EqualTo(new[] { 1, 2, 1 }));
        }

        [Test]
        public void The_newest_foreground_request_replaces_older_queued_ones()
        {
            var fx = new ShellFixture(save: Frontier(9));
            fx.Flow.Play();
            fx.Flow.SelectLevel(1); // starts running
            fx.Flow.BackPressed();
            fx.Flow.SelectLevel(2); // queued behind it
            fx.Flow.BackPressed();
            fx.Flow.SelectLevel(3); // replaces the queued 2
            Assert.That(fx.Runner.Started, Is.EqualTo(1));
            fx.Runner.RunAll();
            Assert.That(fx.Source.Calls.Select(c => c.Number), Is.EqualTo(new[] { 1, 3 }), "level 2 was never generated");
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(fx.Game.Level!.Number, Is.EqualTo(3));
        }

        [Test]
        public void A_failed_prefetch_only_logs_a_warning()
        {
            var fx = new ShellFixture();
            fx.StartLevel(1);
            fx.PlayToEnd();
            fx.Source.Fail = new InvalidOperationException("boom");
            fx.Game.ShowResult();
            fx.Runner.RunAll();
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Result), "the result screen is not disturbed");
            Assert.That(fx.Game.LastError, Is.Null);
            Assert.That(fx.Log.Lines.Single(), Does.StartWith("Warn: pre-generating level 2"));
            Assert.That(fx.Game.NextLevelReady, Is.False);
        }

        // ---- persistence

        [Test]
        public void Flush_writes_progress_and_settings_through_the_sink()
        {
            var fx = new ShellFixture(save: Frontier(3, 9));
            fx.Game.Flush(new PlayerSettings { Sound = false });
            Assert.That(fx.Sink.Progress.Single().CurrentLevel, Is.EqualTo(3));
            Assert.That(fx.Sink.Settings.Single().Sound, Is.False);
            fx.Game.Flush();
            Assert.That(fx.Sink.Settings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_unsubscribes_from_the_flow_and_progress()
        {
            var fx = new ShellFixture();
            fx.Game.Dispose();
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            Assert.That(fx.Runner.Started, Is.EqualTo(0));
            Assert.That(fx.Source.Calls, Is.Empty);
        }
    }
}
