using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Presentation;
using Cascade.Presentation.Shell;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>PRD 5.1 / 5.2 / 5.3: the view-models of every screen. All text on screen comes from here.</summary>
    public class ViewModelTests
    {
        static SaveData Frontier(int level, params (int Level, int Stars)[] stars) =>
            new SaveData
            {
                CurrentLevel = level,
                Stars = StarTable.From(stars.Select(s => new KeyValuePair<int, int>(s.Level, s.Stars))),
            };

        // ---- Fmt

        [Test]
        public void Number_formatting_ignores_the_current_culture()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(Fmt.Int(1234567), Is.EqualTo("1,234,567"));
                Assert.That(Fmt.Fixed(0.5, 2), Is.EqualTo("0.50"));
                Assert.That(Fmt.Round(1.25), Is.EqualTo("1.25"));
                Assert.That(Fmt.Pair(5, 1200), Is.EqualTo("5/1,200"));
                Assert.That(Fmt.Plain(-3), Is.EqualTo("-3"));
                Assert.That(Fmt.TryParseDouble("0.75", out var d) && d == 0.75);
                Assert.That(Fmt.TryParseDouble("0,75", out _), Is.False, "a decimal comma is not accepted");
                Assert.That(Fmt.TryParseInt(" 42 ", out var i) && i == 42);
                Assert.That(Fmt.TryParseDouble("NaN", out _), Is.False);
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        // ---- Title

        [Test]
        public void Title_labels_and_commands()
        {
            var fx = new ShellFixture();
            var vm = new TitleViewModel(fx.Flow, fx.Progress);
            Assert.That((vm.Title, vm.Tagline, vm.PlayLabel, vm.SettingsLabel, vm.QuitLabel),
                Is.EqualTo(("Cascade", "match, connect, relax", "Play", "Settings", "Quit")));
            Assert.That(vm.QuitVisible);

            int changed = 0;
            vm.Changed += () => changed++;
            fx.Progress.RecordWin(1, 3);
            Assert.That(vm.PlayLabel, Is.EqualTo("Continue (level 2)"));
            Assert.That(changed, Is.EqualTo(1));

            Assert.That(vm.OpenSettings());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Settings));
            fx.Flow.ToTitle();
            Assert.That(vm.Play());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
        }

        [Test]
        public void Title_has_no_quit_button_on_android_and_quit_asks_to_leave_on_desktop()
        {
            var phone = new ShellFixture(ShellFixture.Android);
            var vm = new TitleViewModel(phone.Flow, phone.Progress);
            Assert.That(vm.QuitVisible, Is.False);
            Assert.That(vm.Quit(), Is.False);

            var pc = new ShellFixture();
            int leave = 0;
            pc.Flow.LeaveAppRequested += () => leave++;
            Assert.That(new TitleViewModel(pc.Flow, pc.Progress).Quit());
            Assert.That(leave, Is.EqualTo(1));
        }

        [Test]
        public void Disposed_view_models_stop_listening()
        {
            var fx = new ShellFixture();
            var vm = new TitleViewModel(fx.Flow, fx.Progress);
            int changed = 0;
            vm.Changed += () => changed++;
            vm.Dispose();
            fx.Progress.RecordWin(1, 1);
            Assert.That(changed, Is.EqualTo(0));
        }

        // ---- Map

        [Test]
        public void Map_nodes_show_state_stars_and_a_window_around_the_current_level()
        {
            var fx = new ShellFixture(save: Frontier(30, (1, 3), (29, 2), (5, 1)));
            var map = new MapViewModel(fx.Progress, fx.Flow);
            Assert.That((map.CurrentLevel, map.CenterLevel), Is.EqualTo((30, 30)));
            Assert.That((map.FirstLevel, map.LastLevel, map.HighestShown), Is.EqualTo((18, 38, 38)));
            var nodes = map.Nodes;
            Assert.That(nodes.Select(n => n.Number), Is.EqualTo(Enumerable.Range(18, 21)));
            var done = nodes.Single(n => n.Number == 29);
            Assert.That((done.State, done.Stars, done.IsSelectable, done.StarsText, done.Label), Is.EqualTo((MapNodeState.Completed, 2, true, "2/3", "29")));
            var cur = nodes.Single(n => n.Number == 30);
            Assert.That((cur.State, cur.IsCurrent, cur.IsSelectable, cur.Stars, cur.StarsText), Is.EqualTo((MapNodeState.Current, true, true, 0, "")));
            var locked = nodes.Single(n => n.Number == 31);
            Assert.That((locked.State, locked.IsSelectable), Is.EqualTo((MapNodeState.Locked, false)));
            Assert.That(nodes.All(n => n.PathX >= -1 && n.PathX <= 1));
            Assert.That(nodes.Select(n => n.PathX).Distinct().Count(), Is.GreaterThan(10), "the path wobbles");
            Assert.That(map.TotalStarsText, Is.EqualTo("Stars: 6"));
            Assert.That(map.Title, Is.EqualTo("Choose a level"));
        }

        [Test]
        public void Map_at_the_start_shows_the_first_level_and_a_few_locked_ones()
        {
            var fx = new ShellFixture();
            var map = new MapViewModel(fx.Progress, fx.Flow);
            Assert.That(map.Nodes.Select(n => n.Number), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(map.Nodes[0].IsCurrent);
            Assert.That(map.Nodes.Skip(1).All(n => n.State == MapNodeState.Locked));
            Assert.That(map.CanScrollEarlier, Is.False);
            Assert.That(map.CanScrollLater, Is.False);
        }

        [Test]
        public void Map_scrolls_within_bounds_and_snaps_back_when_opened()
        {
            var fx = new ShellFixture(save: Frontier(40));
            var map = new MapViewModel(fx.Progress, fx.Flow, levelsBehind: 5, levelsAhead: 3);
            int changed = 0;
            map.Changed += () => changed++;
            Assert.That((map.FirstLevel, map.LastLevel), Is.EqualTo((35, 43)));
            map.ScrollBy(-20);
            Assert.That(map.CenterLevel, Is.EqualTo(20));
            Assert.That((map.FirstLevel, map.LastLevel), Is.EqualTo((15, 23)));
            Assert.That(map.CanScrollEarlier);
            map.ScrollBy(-100);
            Assert.That(map.CenterLevel, Is.EqualTo(1));
            Assert.That(map.FirstLevel, Is.EqualTo(1));
            map.ScrollBy(500);
            Assert.That(map.CenterLevel, Is.EqualTo(43), "never past the frontier teaser");
            Assert.That(map.CanScrollLater, Is.False);
            map.ScrollBy(0);
            int afterScrolls = changed;
            map.ScrollBy(-10);
            fx.Flow.Play(); // opening the map recenters
            Assert.That(map.CenterLevel, Is.EqualTo(40));
            Assert.That(changed, Is.GreaterThan(afterScrolls));
            map.ScrollBy(-3);
            map.ScrollToCurrent();
            Assert.That(map.CenterLevel, Is.EqualTo(40));
        }

        [Test]
        public void Map_respects_the_maximum_level_and_follows_progress()
        {
            var fx = new ShellFixture(save: Frontier(9));
            var map = new MapViewModel(fx.Progress, fx.Flow, maxLevel: 10);
            Assert.That(map.HighestShown, Is.EqualTo(10));
            Assert.That(map.Nodes.Last().Number, Is.EqualTo(10));
            int changed = 0;
            map.Changed += () => changed++;
            fx.Progress.RecordWin(9, 3);
            Assert.That(map.CurrentLevel, Is.EqualTo(10));
            Assert.That(map.NodeFor(9).Stars, Is.EqualTo(3));
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void Map_select_starts_unlocked_levels_only_and_back_goes_to_the_title()
        {
            var fx = new ShellFixture(save: Frontier(4));
            var map = new MapViewModel(fx.Progress, fx.Flow);
            fx.Flow.Play();
            Assert.That(map.Select(5), Is.False);
            Assert.That(map.Select(0), Is.False);
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(map.Select(2));
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
            fx.Flow.BackPressed();
            Assert.That(map.Back());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Title));
        }

        // ---- Loading and Intro

        [Test]
        public void Loading_says_which_level_is_being_prepared_and_can_be_cancelled()
        {
            var fx = new ShellFixture();
            var vm = new LoadingViewModel(fx.Game);
            Assert.That(vm.Text, Is.EqualTo(""));
            int changed = 0;
            vm.Changed += () => changed++;
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            Assert.That(vm.Text, Is.EqualTo("Preparing level 1"));
            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(vm.Cancel());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(vm.Text, Is.EqualTo(""));
        }

        [Test]
        public void Intro_shows_goals_moves_and_the_mechanic_tooltip_of_an_intro_level()
        {
            var fx = new ShellFixture(save: Frontier(11));
            fx.Source.HardFrom = 99;
            var vm = new IntroViewModel(fx.Game, dev: false);
            Assert.That(vm.HasLevel, Is.False);
            Assert.That(vm.LevelTitle, Is.EqualTo(""));

            fx.Flow.Play();
            fx.Flow.SelectLevel(11);
            fx.Runner.RunAll();
            Assert.That(vm.LevelTitle, Is.EqualTo("Level 11"));
            Assert.That(vm.GoalLines, Is.EqualTo(new[] { "Score 60 points" }));
            Assert.That(vm.MovesText, Is.EqualTo("40 moves"));
            Assert.That(vm.MechanicHeadline, Is.Null);
            Assert.That(vm.MechanicTips, Is.Empty);
            Assert.That(vm.DevInfo, Is.Null);
            Assert.That((vm.GoalsHeader, vm.PlayLabel, vm.BackLabel), Is.EqualTo(("Goals", "Play", "Back")));
            Assert.That(vm.Play());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Play));
        }

        [Test]
        public void Intro_uses_the_generated_intro_text_and_dev_info_only_in_dev_builds()
        {
            var level = FakeLevelSource.Easy(11, 4242, "Collect goals and Line pieces");
            var dev = new ShellFixture(makeSource: _ => new FixedSource(level));
            var devVm = new IntroViewModel(dev.Game, dev: true);
            dev.Flow.Play();
            dev.Flow.DevJump(11);
            dev.Runner.RunAll();
            Assert.That(devVm.MechanicHeadline, Is.EqualTo("New: Collect goals and Line pieces"));
            Assert.That(devVm.MechanicTips.Count, Is.EqualTo(2));
            Assert.That(devVm.MechanicTips[0], Does.Contain("named colors"));
            Assert.That(devVm.MechanicTips[1], Does.Contain("Line piece"));
            Assert.That(devVm.DevInfo, Is.EqualTo("seed 4242  difficulty 0.25  target win 0.90"));

            var release = new ShellFixture(ShellFixture.DesktopRelease, makeSource: _ => new FixedSource(level));
            var relVm = new IntroViewModel(release.Game, dev: release.Host.Dev);
            release.Flow.Play();
            release.Flow.SelectLevel(1);
            release.Runner.RunAll();
            Assert.That(relVm.DevInfo, Is.Null);
            Assert.That(relVm.MechanicHeadline, Is.Not.Null);
        }

        [Test]
        public void Intro_collect_goals_use_the_style_color_names_and_one_move_is_singular()
        {
            var level = new Level(12, 1, 6, 6, new (int, int)[0], 4, new[] { Goal.Collect(12, 0, 2) }, 1, 5, 6, new string[0], null, 0.5, 0.5, null, 1);
            var fx = new ShellFixture(makeSource: _ => new FixedSource(level));
            var vm = new IntroViewModel(fx.Game, dev: false);
            fx.Flow.Play();
            fx.Flow.DevJump(12);
            fx.Runner.RunAll();
            Assert.That(vm.GoalLines.Single(), Is.EqualTo("Clear 12 Red / Green pieces"));
            Assert.That(vm.MovesText, Is.EqualTo("1 move"));
        }

        sealed class FixedSource : Cascade.Generation.ILevelSource
        {
            readonly Level _level;
            public FixedSource(Level level) { _level = level; }
            public Level Get(int levelNumber, ulong seed) => _level;
        }

        [Test]
        public void Mechanic_tips_cover_every_unlock_name_the_profile_can_produce()
        {
            foreach (var name in new[] { "Collect goals", "Line pieces", "Bomb pieces", "Shaped boards", "Color pieces", "Combined goals" })
                Assert.That(MechanicTexts.For(name), Is.Not.Null.And.Not.Empty, name);
            Assert.That(MechanicTexts.For("Mystery"), Is.Null);
            Assert.That(MechanicTexts.ForIntro(null), Is.Empty);
            Assert.That(MechanicTexts.ForIntro("Mystery and Line pieces").Count, Is.EqualTo(1));

            // and the real profile's intro levels produce text the tips know
            var profile = TestDials.Profile();
            int known = 0;
            for (int n = 1; n <= 80; n++)
            {
                var p = profile.Evaluate(n);
                if (p.Intro == null) continue;
                Assert.That(MechanicTexts.ForIntro(p.Intro), Is.Not.Empty, "level " + n + ": " + p.Intro);
                known++;
            }
            Assert.That(known, Is.EqualTo(5), "levels 11, 21, 36, 51 and 76");
        }

        // ---- HUD

        [Test]
        public void Hud_moves_update_at_once_but_score_and_goals_wait_for_the_view()
        {
            var fx = new ShellFixture();
            fx.Source.HardFrom = 1; // 2 moves, big goals
            var hud = new HudViewModel(fx.Game);
            fx.StartLevel(1);
            Assert.That((hud.LevelTitle, hud.MovesText, hud.ScoreText), Is.EqualTo(("Level 1", "2", "0")));
            Assert.That(hud.Goals.Select(g => g.ProgressText), Is.EqualTo(new[] { "0/1,000,000", "0/500" }));
            Assert.That(hud.Goals[0].Text, Is.EqualTo("Score 1,000,000 points"));
            Assert.That(hud.Goals[1].Text, Is.EqualTo("Clear 500 Red / Blue pieces"));
            Assert.That((hud.MovesLabel, hud.ScoreLabel, hud.HintLabel, hud.PauseLabel), Is.EqualTo(("MOVES", "SCORE", "Hint", "Pause")));

            int changed = 0;
            hud.Changed += () => changed++;
            var hint = fx.Game.Hint()!.Value;
            var r = fx.Game.TrySwap(hint.A, hint.B)!;
            Assert.That(r.Played);
            Assert.That(hud.MovesText, Is.EqualTo("1"), "moves follow the swap");
            Assert.That(hud.ShownScore, Is.EqualTo(0), "score waits for the animation");
            Assert.That(hud.Goals[0].Progress, Is.EqualTo(0));
            Assert.That(changed, Is.GreaterThan(0));

            hud.AddScore(20);
            Assert.That(hud.ScoreText, Is.EqualTo("20"));
            hud.AddScore(1_000_000);
            Assert.That(hud.ShownScore, Is.EqualTo(fx.Game.Session!.Score), "the shown score never passes the real one");
            hud.Sync();
            Assert.That(hud.Goals[0].Progress, Is.EqualTo(fx.Game.Session.Score));
            Assert.That(hud.Goals[0].Fraction, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(hud.Goals[0].Done, Is.False);
        }

        [Test]
        public void Hud_star_thresholds_and_provisional_stars_follow_the_shown_score()
        {
            var level = new Level(1, 1, 6, 6, new (int, int)[0], 4, new[] { Goal.Score(100) }, 20, 400, 800, new string[0], null, 0.5, 0.5, null, 1);
            var fx = new ShellFixture(makeSource: _ => new FixedSource(level));
            var hud = new HudViewModel(fx.Game);
            Assert.That(hud.StarMarkers, Is.Empty);
            fx.Flow.Play();
            fx.Flow.SelectLevel(1);
            fx.Runner.RunAll();
            Assert.That(hud.StarMarkers.Select(m => (m.Stars, m.Score, m.Reached, m.Text)), Is.EqualTo(new[] { (2, 400, false, "400"), (3, 800, false, "800") }));
            Assert.That(hud.ScoreFraction, Is.EqualTo(0));
            Assert.That(hud.Star2Fraction, Is.EqualTo(0.5));
            Assert.That(hud.ProvisionalStars, Is.EqualTo(1));
        }

        [Test]
        public void Hud_syncs_by_itself_when_a_new_level_is_ready()
        {
            var fx = new ShellFixture();
            var hud = new HudViewModel(fx.Game);
            fx.StartLevel(1);
            fx.PlayToEnd();
            fx.Game.ShowResult();
            fx.Flow.ToMap();
            fx.Flow.SelectLevel(1);
            fx.Runner.RunAll();
            Assert.That(hud.MovesText, Is.EqualTo("40"));
            Assert.That(hud.ScoreText, Is.EqualTo("0"));
        }

        // ---- Pause

        [Test]
        public void Pause_overlay_follows_the_flow()
        {
            var fx = new ShellFixture();
            var vm = new PauseViewModel(fx.Flow);
            Assert.That(vm.IsOpen, Is.False);
            fx.StartLevel(1);
            int changed = 0;
            vm.Changed += () => changed++;
            Assert.That(vm.Pause());
            Assert.That(vm.IsOpen);
            Assert.That((vm.Title, vm.ResumeLabel, vm.QuitLabel, vm.PauseButtonLabel), Is.EqualTo(("Paused", "Resume", "Quit to map", "Pause")));
            Assert.That(vm.Resume());
            Assert.That(vm.IsOpen, Is.False);
            vm.Pause();
            Assert.That(vm.QuitToMap());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Map));
            Assert.That(vm.IsOpen, Is.False);
            Assert.That(changed, Is.GreaterThanOrEqualTo(4));
        }

        // ---- Result

        [Test]
        public void Result_after_a_win_shows_stars_score_and_next()
        {
            var fx = new ShellFixture();
            var vm = new ResultViewModel(fx.Game);
            Assert.That(vm.HasResult, Is.False);
            fx.StartLevel(1);
            fx.PlayToEnd();
            fx.Game.ShowResult();

            Assert.That((vm.HasResult, vm.Won, vm.Headline, vm.Stars, vm.ShowStars, vm.LevelNumber), Is.EqualTo((true, true, "Level complete!", 2, true, 1)));
            Assert.That(vm.StarSlots, Is.EqualTo(new[] { true, true, false }));
            Assert.That(vm.ScoreText, Does.StartWith("Score "));
            Assert.That(vm.ScoreText, Is.EqualTo("Score " + fx.Game.Session!.Score.ToString("N0", CultureInfo.InvariantCulture)));
            Assert.That(vm.NewBestText, Is.EqualTo("New best!"));
            Assert.That((vm.NextAvailable, vm.RetryAvailable, vm.MapAvailable), Is.EqualTo((true, false, true)));
            Assert.That((vm.NextLabel, vm.RetryLabel, vm.MapLabel, vm.NextLevelNumber), Is.EqualTo(("Next level", "Retry", "Map", 2)));
            Assert.That(vm.GoalLines, Is.Empty);
            Assert.That(vm.NextLevelReady, Is.False);
            fx.Runner.RunAll();
            Assert.That(vm.NextLevelReady, "pre-generated while the result showed");
            Assert.That(vm.Retry(), Is.False);
            Assert.That(vm.Next());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
        }

        [Test]
        public void Result_after_a_loss_shows_the_goals_and_retry()
        {
            var fx = new ShellFixture(save: Frontier(2));
            fx.Source.HardFrom = 1;
            var vm = new ResultViewModel(fx.Game);
            fx.StartLevel(2);
            fx.PlayToEnd();
            fx.Game.ShowResult();
            Assert.That((vm.Won, vm.Headline, vm.Stars, vm.ShowStars), Is.EqualTo((false, "Out of moves", 0, false)));
            Assert.That(vm.NewBestText, Is.Null);
            Assert.That(vm.StarSlots, Is.EqualTo(new[] { false, false, false }));
            Assert.That(vm.GoalLines.Count, Is.EqualTo(2));
            Assert.That(vm.GoalLines[0], Does.StartWith("Score 1,000,000 points  ("));
            Assert.That(vm.GoalLines[0], Does.EndWith("/1,000,000)"));
            Assert.That((vm.NextAvailable, vm.RetryAvailable, vm.MapAvailable), Is.EqualTo((false, true, true)));
            Assert.That(vm.Next(), Is.False);
            Assert.That(vm.Retry());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
            fx.Runner.RunAll();
            fx.Flow.BackPressed();
            Assert.That(vm.ToMap() || fx.Flow.Current == ScreenId.Map);
        }

        // ---- Settings

        [Test]
        public void Settings_apply_live_and_are_saved_through_the_sink()
        {
            var fx = new ShellFixture();
            var vm = new SettingsViewModel(fx.Settings, fx.Sink, fx.Flow);
            Assert.That((vm.Title, vm.BackLabel, vm.SpeedMin, vm.SpeedMax), Is.EqualTo(("Settings", "Back", 0.5, 3.0)));
            Assert.That((vm.SoundText, vm.AdaptiveText, vm.SpeedText), Is.EqualTo(("Sound: on", "Adaptive difficulty: on", "Animation speed x1.00")));

            int changed = 0, timing = 0;
            vm.Changed += () => changed++;
            fx.Settings.TimingChanged += _ => timing++;
            vm.SetAnimationSpeed(2.0);
            Assert.That(vm.AnimationSpeed, Is.EqualTo(2.0));
            Assert.That(vm.SpeedText, Is.EqualTo("Animation speed x2.00"));
            Assert.That(fx.Settings.Timing.Speed, Is.EqualTo(2.0), "the board animation follows at once");
            Assert.That(fx.Settings.Timing.Scaled(300), Is.EqualTo(150));
            Assert.That(timing, Is.EqualTo(1));
            Assert.That(fx.Sink.Settings.Last().AnimationSpeed, Is.EqualTo(2.0));

            vm.ToggleSound();
            vm.ToggleAdaptive();
            Assert.That((vm.SoundText, vm.AdaptiveText), Is.EqualTo(("Sound: off", "Adaptive difficulty: off")));
            var saved = fx.Sink.Settings.Last();
            Assert.That((saved.Sound, saved.Adaptive, saved.AnimationSpeed), Is.EqualTo((false, false, 2.0)));
            Assert.That(fx.Sink.Settings.Count, Is.EqualTo(3));
            Assert.That(changed, Is.EqualTo(3));
            Assert.That(timing, Is.EqualTo(1), "sound does not touch the timing");
        }

        [Test]
        public void Settings_speed_is_clamped_rounded_and_a_no_op_is_not_saved()
        {
            var fx = new ShellFixture();
            var vm = new SettingsViewModel(fx.Settings, fx.Sink, fx.Flow);
            vm.SetAnimationSpeed(9);
            Assert.That(vm.AnimationSpeed, Is.EqualTo(3.0));
            vm.SetAnimationSpeed(0.01);
            Assert.That(vm.AnimationSpeed, Is.EqualTo(0.5));
            vm.SetAnimationSpeed(1.2345);
            Assert.That(vm.AnimationSpeed, Is.EqualTo(1.23));
            int saves = fx.Sink.Settings.Count;
            vm.SetAnimationSpeed(1.2301);
            vm.SetAnimationSpeed(double.NaN);
            vm.SetSound(true);
            Assert.That(fx.Sink.Settings.Count, Is.EqualTo(saves), "no change, nothing written");
            Assert.That(vm.Back(), Is.False, "not on the settings page");
            fx.Flow.OpenSettings();
            Assert.That(vm.Back());
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Title));
        }

        [Test]
        public void Live_settings_follow_animation_dial_edits_and_clamp_bad_stored_values()
        {
            var fx = new ShellFixture(settings: new PlayerSettings { AnimationSpeed = 99 });
            Assert.That(fx.Settings.Player.AnimationSpeed, Is.EqualTo(3.0));
            int timing = 0;
            fx.Settings.TimingChanged += _ => timing++;
            fx.Dials.Set("animation", "swap", 300);
            Assert.That(fx.Settings.Timing.SwapMs, Is.EqualTo(300));
            Assert.That(timing, Is.EqualTo(1));
            fx.Dials.Set("scoring", "run3", 70);
            Assert.That(timing, Is.EqualTo(1), "other sections are not the timing's business");
            fx.Settings.Dispose();
            fx.Dials.Set("animation", "swap", 310);
            Assert.That(timing, Is.EqualTo(1));
        }

        // ---- Dev page

        [Test]
        public void Auto_play_has_a_plan_ahead_mode_that_is_off_by_default()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            Assert.That(dev.AutoPlayPlans, Is.False);
            Assert.That(dev.ToggleAutoPlayPlans());
            Assert.That(dev.AutoPlayPlans);
            Assert.That(dev.SetAutoPlayPlans(true), Is.False, "no change, no event");
            dev.SetAutoPlayPlans(false);
            Assert.That(dev.AutoPlayPlans, Is.False);
        }

        [Test]
        public void Dev_page_is_generated_from_the_dial_metadata()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            Assert.That(dev.Enabled);
            Assert.That(dev.Sections.Select(s => s.Name), Is.EqualTo(new[]
                { "scoring", "specials", "stars", "input", "render", "animation", "celebration", "audio", "camera", "pieces", "generator", "planner" }));
            Assert.That(dev.Sections.Sum(s => s.Fields.Count), Is.EqualTo(fx.Dials.Metadata.Count), "every dial is on the page");
            Assert.That(dev.SelectedSection, Is.EqualTo("scoring"));

            var f = dev.Field("scoring.run3")!;
            Assert.That((f.Section, f.Key, f.Path, f.IsInt, f.Min, f.Max, f.Step), Is.EqualTo(("scoring", "run3", "scoring.run3", true, 0.0, 10000.0, 5.0)));
            Assert.That((f.CurrentText, f.DefaultText, f.IsChanged, f.RangeText), Is.EqualTo(("60", "60", false, "0 to 10000, step 5")));
            Assert.That(f.Description, Is.Not.Empty);
            var step = dev.Field("scoring.cascade_step")!;
            Assert.That((step.IsInt, step.Type, step.CurrentText), Is.EqualTo((false, DialType.Double, "0.5")));
            Assert.That(dev.Field("nope.nope"), Is.Null);

            // the new touch and render dials are there
            foreach (var path in new[] { "input.touch_inner_fraction", "input.double_tap_ms", "input.tap_slop_mm", "input.pinch_min_mm",
                "input.min_touch_target_mm", "render.multisamples", "render.target_fps" })
                Assert.That(dev.Field(path), Is.Not.Null, path);
        }

        [Test]
        public void Dev_page_edits_apply_live_flag_changes_and_report_problems()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            int changed = 0;
            dev.Changed += () => changed++;

            Assert.That(dev.Set("scoring.run3", "80"));
            Assert.That(fx.Dials.Scoring.Run3, Is.EqualTo(80));
            Assert.That(dev.Message, Is.EqualTo("scoring.run3 = 80"));
            Assert.That(dev.Field("scoring.run3")!.IsChanged);
            Assert.That(dev.Selected!.ChangedCount, Is.EqualTo(1));
            Assert.That(dev.ChangedCount, Is.EqualTo(1));
            Assert.That(changed, Is.GreaterThan(0));

            Assert.That(dev.Set("scoring.run3", "99999999"), Is.False);
            Assert.That(dev.Message, Does.StartWith("rejected: ").And.Contain("out of range"));
            Assert.That(fx.Dials.Scoring.Run3, Is.EqualTo(80), "a rejected value changes nothing");
            Assert.That(dev.Set("scoring.run3", "12.5"), Is.False);
            Assert.That(dev.Message, Is.EqualTo("scoring.run3: enter a whole number"));
            Assert.That(dev.Set("scoring.cascade_step", "abc"), Is.False);
            Assert.That(dev.Message, Is.EqualTo("scoring.cascade_step: enter a number"));
            Assert.That(dev.Set("scoring.cascade_step", "0.75"));
            Assert.That(fx.Dials.Scoring.CascadeStep, Is.EqualTo(0.75));
            Assert.That(dev.Set("no.such", "1"), Is.False);
            Assert.That(dev.Message, Is.EqualTo("unknown dial no.such"));

            Assert.That(dev.Nudge("scoring.run3", +1));
            Assert.That(fx.Dials.Scoring.Run3, Is.EqualTo(85));
            Assert.That(dev.Nudge("scoring.run3", -1));
            Assert.That(dev.Nudge("scoring.run3", -1));
            Assert.That(fx.Dials.Scoring.Run3, Is.EqualTo(75));
            dev.Set("scoring.cascade_step", "0.1");
            dev.Nudge("scoring.cascade_step", +1);
            Assert.That(fx.Dials.Scoring.CascadeStep, Is.EqualTo(0.15), "no floating point dust");
            dev.Set("scoring.cascade_step", "5");
            Assert.That(dev.Nudge("scoring.cascade_step", +1), Is.True, "clamped at the maximum");
            Assert.That(fx.Dials.Scoring.CascadeStep, Is.EqualTo(5.0));

            Assert.That(dev.ResetField("scoring.run3"));
            Assert.That(fx.Dials.Scoring.Run3, Is.EqualTo(60));
            Assert.That(dev.Message, Is.EqualTo("scoring.run3 reset to 60"));
            Assert.That(dev.ResetAll());
            Assert.That(dev.ChangedCount, Is.EqualTo(0));
            Assert.That(dev.Message, Is.EqualTo("reset to defaults (not saved)"));
        }

        [Test]
        public void Dev_page_numbers_are_typed_and_shown_in_invariant_culture()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                var fx = new ShellFixture();
                var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
                Assert.That(dev.Set("scoring.cascade_step", "0.25"));
                Assert.That(dev.Field("scoring.cascade_step")!.CurrentText, Is.EqualTo("0.25"));
                Assert.That(dev.Set("scoring.cascade_step", "0,25"), Is.False);
                Assert.That(dev.Field("scoring.cascade_step")!.RangeText, Is.EqualTo("0 to 5, step 0.05"));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Test]
        public void Dev_page_save_writes_only_the_changed_dials()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            dev.Set("camera.pitch_deg", "40");
            dev.Set("scoring.cascade_step", "0.75");
            Assert.That(dev.Save());
            Assert.That(dev.Message, Is.EqualTo("saved 2 changed dials to dev_dials.toml"));
            var text = fx.Storage.GetText("dev_dials.toml")!;
            Assert.That(text, Does.Contain("pitch_deg = 40").And.Contain("cascade_step = 0.75"));
            Assert.That(text, Does.Not.Contain("run3"));

            // and the registry reloads it
            var data = new DictionaryDataSource().With("dials.toml", RepoFiles.ReadData("dials.toml"));
            var reloaded = DialRegistry.Load(data, fx.Host, fx.Storage);
            Assert.That(reloaded.Camera.PitchDeg, Is.EqualTo(40));
        }

        [Test]
        public void Dev_page_save_without_storage_reports_the_failure()
        {
            var data = new DictionaryDataSource().With("dials.toml", RepoFiles.ReadData("dials.toml"));
            var fx = new ShellFixture();
            var noStorage = DialRegistry.Load(data, fx.Host);
            var dev = new DevViewModel(noStorage, fx.Game, fx.Host);
            Assert.That(dev.Save(), Is.False);
            Assert.That(dev.Message, Does.StartWith("save failed: "));
        }

        [Test]
        public void Dev_page_selects_sections()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            dev.Set("scoring.run3", "x");
            Assert.That(dev.SelectSection("generator"));
            Assert.That(dev.SelectedSection, Is.EqualTo("generator"));
            Assert.That(dev.Selected!.Fields.All(f => f.Section == "generator"));
            Assert.That(dev.Message, Is.EqualTo(""), "switching tabs clears the message");
            Assert.That(dev.SelectSection("generator"), Is.False);
            Assert.That(dev.SelectSection("nonsense"), Is.False);
        }

        [Test]
        public void Dev_page_shows_the_level_info_and_jumps_to_a_level()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            Assert.That(dev.HasLevel, Is.False);
            Assert.That(dev.LevelInfoText, Is.EqualTo("no level running"));
            Assert.That((dev.SeedText, dev.DifficultyText, dev.TargetWinRateText, dev.MeasuredWinRateText), Is.EqualTo(("-", "-", "-", "-")));

            fx.StartLevel(1);
            Assert.That(dev.HasLevel);
            Assert.That(dev.LevelNumber, Is.EqualTo(1));
            Assert.That(dev.SeedText, Is.EqualTo("1000"));
            Assert.That(dev.LevelInfoText, Is.EqualTo("level 1  seed 1000  difficulty 0.25  target 0.90  measured 0.88"));

            fx.Source.HardFrom = 1;
            fx.Flow.OpenDev();
            Assert.That(dev.IsOpen);
            Assert.That(dev.JumpToLevel("30"));
            Assert.That(dev.IsOpen, Is.False, "the page closes on a jump");
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Loading));
            Assert.That(fx.Game.PreparingText, Is.EqualTo("Preparing level 30"));
            fx.Runner.RunAll();
            Assert.That(fx.Game.Level!.Number, Is.EqualTo(30));
            Assert.That(dev.LevelInfoText, Does.StartWith("level 30  seed 1001  difficulty 0.75  target 0.40  measured -"));
            Assert.That(fx.Progress.CurrentLevel, Is.EqualTo(1), "a jump does not change progress");
        }

        [Test]
        public void Dev_page_jump_input_is_validated_and_clamped_to_level_one()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            fx.Flow.OpenDev();
            Assert.That(dev.JumpToLevel("abc"), Is.False);
            Assert.That(dev.Message, Is.EqualTo("enter a level number"));
            Assert.That(dev.JumpToLevel(""), Is.False);
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Title));
            Assert.That(dev.JumpToLevel(-4));
            Assert.That(fx.Flow.LevelNumber, Is.EqualTo(1));
        }

        [Test]
        public void Dev_page_is_inert_in_a_release_build()
        {
            var fx = new ShellFixture(ShellFixture.DesktopRelease);
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            Assert.That(dev.Enabled, Is.False);
            Assert.That(dev.Sections, Is.Empty);
            Assert.That(dev.Selected, Is.Null);
            Assert.That(dev.Field("scoring.run3"), Is.Null);
            Assert.That(dev.Set("scoring.run3", "80"), Is.False);
            Assert.That(fx.Dials.Scoring.Run3, Is.EqualTo(60));
            Assert.That(dev.Nudge("scoring.run3", 1), Is.False);
            Assert.That(dev.ResetField("scoring.run3"), Is.False);
            Assert.That(dev.ResetAll(), Is.False);
            Assert.That(dev.Save(), Is.False);
            Assert.That(fx.Storage.Writes, Is.EqualTo(0));
            Assert.That(dev.SelectSection("scoring"), Is.False);
            Assert.That(dev.JumpToLevel(5), Is.False);
            Assert.That(dev.JumpToLevel("5"), Is.False);
            Assert.That(dev.Toggle(), Is.False);
            Assert.That(dev.IsOpen, Is.False);
            Assert.That(dev.HasLevel, Is.False);
            Assert.That(fx.Flow.Current, Is.EqualTo(ScreenId.Title));
        }

        [Test]
        public void Dev_page_forwards_changes_from_dials_the_game_and_the_flow()
        {
            var fx = new ShellFixture();
            var dev = new DevViewModel(fx.Dials, fx.Game, fx.Host);
            int changed = 0;
            dev.Changed += () => changed++;
            fx.Dials.Set("scoring", "run3", 70); // changed elsewhere, for example by a platform hook
            int afterDial = changed;
            Assert.That(afterDial, Is.GreaterThan(0));
            fx.Flow.OpenDev();
            Assert.That(changed, Is.GreaterThan(afterDial));
            Assert.That(dev.Toggle());
            Assert.That(dev.IsOpen, Is.False);
            dev.Dispose();
            int frozen = changed;
            fx.Dials.Set("scoring", "run3", 80);
            Assert.That(changed, Is.EqualTo(frozen));
        }
    }
}
