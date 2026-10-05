using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Persistence;
using Cascade.Platform.Abstractions;
using Cascade.Presentation.Shell;
using Cascade.Unity.Platform;
using Cascade.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cascade.Tests.Play
{
    /// <summary>
    /// U5 acceptance: the whole shell driven the way a player drives it (UI Toolkit button events, the Input System keyboard and mouse,
    /// the real lifecycle bridge) over a real <c>FileStorage</c> in a temporary folder, with a fresh boot to prove what was saved.
    /// </summary>
    public class ShellTests
    {
        PlayHarness h = null!;

        static HostInfo Phone(HostInfo host) => host with { Kind = PlatformKind.Android, HasTouch = true, HasMouse = false, ScreenDpi = 420f };

        [UnityTearDown]
        public IEnumerator TearDown() => h.TearDown();

        IEnumerator Boot(int w = 1280, int hgt = 800, System.Func<HostInfo, HostInfo>? host = null, bool gated = false)
        {
            h = new PlayHarness();
            yield return h.SetUp(w, hgt, host, gated);
            h.App.Live.Update(h.App.Live.Player with { AnimationSpeed = 3.0 }); // quick animations; nothing here is about their look
            yield return PlayHarness.UiSettled();
        }

        SaveData OnDisk() => new SaveService(new FileStorage(h.DataDir)).LoadSave();

        string Fingerprint()
        {
            var sb = new StringBuilder();
            var board = h.Session.Board;
            foreach (var c in board.Topology.Cells)
            {
                var t = board.TileAt(c);
                sb.Append(c.Value).Append(':').Append(t?.Color).Append(t?.Special != null ? "s" : "").Append(' ');
            }
            return sb.ToString();
        }

        IEnumerator PlayBest()
        {
            while (h.Session.IsPlaying)
            {
                var move = h.Session.Hint();
                Assert.That(move, Is.Not.Null);
                h.Controller.RequestSwap(move!.Value.A, move.Value.B);
                yield return h.Settled();
                yield return PlayHarness.Frames(1);
            }
            yield return h.WaitForResult();
        }

        IEnumerator PlayWorst()
        {
            while (h.Session.IsPlaying)
            {
                var moves = h.Session.LegalMoves;
                var worst = moves[0];
                int low = int.MaxValue;
                foreach (var m in moves)
                {
                    var copy = h.Session.Copy();
                    copy.TrySwap(m.A, m.B);
                    int gain = copy.Score + copy.TilesCollected * 1000;
                    if (gain < low) { low = gain; worst = m; }
                }
                h.Controller.RequestSwap(worst.A, worst.B);
                yield return h.Settled();
                yield return PlayHarness.Frames(1);
            }
            yield return h.WaitForResult();
        }

        // ---- title and back ----

        [UnityTest]
        public IEnumerator The_title_is_first_and_desktop_has_Quit_but_Esc_does_not_leave()
        {
            yield return Boot();
            Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Title));
            Assert.That(h.Ui.IsPageShown(ScreenId.Title), Is.True);
            Assert.That(h.Ui.Title.NameLabel.text, Is.EqualTo(h.App.Models.Title.Title));
            Assert.That(h.Ui.Title.PlayButton.text, Is.EqualTo("Play"));
            Assert.That(h.Ui.Title.QuitButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            bool quit = false;
            h.App.QuitRequested += () => quit = true;
            yield return h.Key(h.Keyboard.escapeKey);
            Assert.That(quit, Is.False, "Esc on the desktop title does nothing");
            yield return h.ClickElement(h.Ui.Title.QuitButton);
            Assert.That(quit, Is.True, "the Quit button leaves the app");
        }

        [UnityTest]
        public IEnumerator On_a_phone_the_title_has_no_Quit_and_Back_leaves_the_app()
        {
            yield return Boot(host: Phone);
            Assert.That(h.App.Host.Kind, Is.EqualTo(PlatformKind.Android));
            Assert.That(h.Ui.Title.QuitButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            bool quit = false;
            h.App.QuitRequested += () => quit = true;
            yield return h.Key(h.Keyboard.escapeKey);
            Assert.That(quit, Is.True, "Back on the Android title quits");
        }

        [UnityTest]
        public IEnumerator Back_goes_one_step_up_everywhere_and_pauses_in_play()
        {
            yield return Boot(gated: true);
            h.Gate!.Hold();
            h.App.Progress.Load(new SaveData { CurrentLevel = 14 });
            yield return h.ClickElement(h.Ui.Title.SettingsButton);
            yield return h.WaitForScreen(ScreenId.Settings);
            yield return h.Key(h.Keyboard.escapeKey);
            yield return h.WaitForScreen(ScreenId.Title);
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            yield return h.Key(h.Keyboard.escapeKey);
            yield return h.WaitForScreen(ScreenId.Title);

            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            yield return h.ClickElement(h.Ui.Map.NodeButton(5)!);
            yield return h.WaitForScreen(ScreenId.Loading);
            yield return h.Key(h.Keyboard.escapeKey);
            yield return h.WaitForScreen(ScreenId.Map);
            h.Gate.Release();
            yield return PlayHarness.Seconds(1f);
            Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Map), "a level that finishes after Back does not pull the player away");

            yield return h.ClickElement(h.Ui.Map.NodeButton(5)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.Key(h.Keyboard.escapeKey);
            yield return h.WaitForScreen(ScreenId.Map);

            yield return h.ClickElement(h.Ui.Map.NodeButton(5)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            yield return h.Key(h.Keyboard.escapeKey);
            yield return PlayHarness.UiSettled();
            Assert.That(h.Flow.Paused, Is.True);
            Assert.That(h.Ui.IsPauseShown, Is.True, "Esc in play shows the pause overlay");
            Assert.That(h.Controller.InputEnabled, Is.False, "the board takes no input while paused");
            yield return h.Key(h.Keyboard.escapeKey);
            yield return PlayHarness.UiSettled();
            Assert.That(h.Flow.Paused, Is.False);
            Assert.That(h.Ui.IsPauseShown, Is.False);
            Assert.That(h.Controller.InputEnabled, Is.True);

            // the dev page is closed first
            yield return h.Key(h.Keyboard.f1Key);
            Assert.That(h.Flow.DevOpen, Is.True);
            Assert.That(h.Controller.InputEnabled, Is.False, "the board takes no input under the dev page");
            yield return h.Key(h.Keyboard.escapeKey);
            Assert.That(h.Flow.DevOpen, Is.False);
            Assert.That(h.Flow.Paused, Is.False, "Esc closed the dev page and did not pause");
        }

        // ---- the journey ----

        [UnityTest]
        public IEnumerator Title_to_play_and_back_through_map_loading_intro_pause_and_quit()
        {
            yield return Boot(gated: true);
            h.Gate!.Hold();
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            Assert.That(h.Ui.Map.NodeButton(1), Is.Not.Null);
            Assert.That(h.Ui.Map.NodeElement(1)!.ClassListContains("map-node--current"), Is.True, "level 1 is the current level");
            Assert.That(h.Ui.Map.NodeElement(2)!.ClassListContains("map-node--locked"), Is.True);

            yield return h.ClickElement(h.Ui.Map.NodeButton(2)!);
            yield return PlayHarness.Frames(3);
            Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Map), "a locked level does nothing");

            yield return h.ClickElement(h.Ui.Map.NodeButton(1)!);
            yield return h.WaitForScreen(ScreenId.Loading);
            Assert.That(h.Ui.Loading.TextLabel.text, Is.EqualTo("Preparing level 1"));
            h.Gate.Release();
            yield return h.WaitForScreen(ScreenId.Intro);
            var intro = h.App.Models.Intro;
            Assert.That(h.Ui.Intro.TitleLabel.text, Is.EqualTo(intro.LevelTitle));
            Assert.That(h.Ui.Intro.PlayButton.text, Is.EqualTo(intro.PlayLabel));

            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            yield return PlayHarness.WaitUntil(() => h.App.Rig.HasSlot, 5, "slot");
            Assert.That(h.App.Stage.Active, Is.True, "the board exists in play");
            string first = Fingerprint();
            var (a, b) = h.FirstLegal();
            int moves = h.Session.MovesLeft;
            yield return h.Drag(a, b);
            yield return h.Settled();
            Assert.That(h.Session.MovesLeft, Is.EqualTo(moves - 1));
            Assert.That(h.Hud.MovesLabel.text, Is.EqualTo(h.Session.MovesLeft.ToString()));
            Assert.That(h.Hud.ScoreLabel.text, Is.EqualTo(Fmt.Int(h.Session.Score)), "the HUD score caught up with the animation");

            yield return h.ClickElement(h.Hud.PauseButton);
            yield return PlayHarness.UiSettled();
            Assert.That(h.Ui.IsPauseShown, Is.True);
            yield return h.ClickElement(h.Hud.ResumeButton);
            yield return PlayHarness.UiSettled();
            Assert.That(h.Ui.IsPauseShown, Is.False);
            yield return h.ClickElement(h.Hud.PauseButton);
            yield return h.ClickElement(h.Hud.QuitButton);
            yield return h.WaitForScreen(ScreenId.Map);
            Assert.That(h.App.Stage.Active, Is.False, "the board is gone outside play");

            // the same level again: the frontier keeps its seed, so it is the same board
            yield return h.ClickElement(h.Ui.Map.NodeButton(1)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            Assert.That(Fingerprint(), Is.EqualTo(first), "starting the frontier level again gives the same board");
        }

        [UnityTest]
        public IEnumerator A_win_records_stars_saves_prefetches_the_next_level_and_survives_a_restart()
        {
            yield return Boot();
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            yield return h.ClickElement(h.Ui.Map.NodeButton(1)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            yield return PlayBest();
            Assert.That(h.Flow.Won, Is.True, "the best moves win level 1");
            int stars = h.Session.Stars;
            Assert.That(stars, Is.InRange(1, 3));
            yield return PlayHarness.Seconds(0.8f);
            Assert.That(h.Ui.Result.Stars.Count(s => s.Earned), Is.EqualTo(stars), "the result page draws the stars earned");
            Assert.That(h.Ui.Result.NextButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(h.Ui.Result.RetryButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(h.Ui.Result.ScoreLabel.text, Is.EqualTo("Score " + Fmt.Int(h.Session.Score)));

            var saved = OnDisk();
            Assert.That(File.Exists(Path.Combine(h.DataDir, SaveService.SaveFile)), Is.True, "save.json was written");
            Assert.That(saved.CurrentLevel, Is.EqualTo(2));
            Assert.That(saved.Stars.Get(1), Is.EqualTo(stars));
            ulong seed2 = saved.CurrentSeed!.Value;
            yield return PlayHarness.WaitUntil(() => h.Game.NextLevelReady, 60, "the next level to be pre-generated");

            yield return h.ClickElement(h.Ui.Result.NextButton);
            yield return h.WaitForScreen(ScreenId.Intro);
            Assert.That(h.Game.Level!.Number, Is.EqualTo(2));
            Assert.That(h.Game.Level.Seed, Is.EqualTo(seed2), "the next level uses the seed that was saved");

            // a new process
            yield return h.Reboot();
            Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Title));
            Assert.That(h.Ui.Title.PlayButton.text, Is.EqualTo("Continue (level 2)"));
            Assert.That(h.App.Progress.StarsFor(1), Is.EqualTo(stars));
            Assert.That(h.App.Progress.Data.CurrentSeed, Is.EqualTo(seed2));
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            var earned = h.Ui.Map.NodeElement(1)!.Query<StarElement>().ToList().Count(s => s.Earned);
            Assert.That(earned, Is.EqualTo(stars), "the map shows the stars after a restart");
            yield return h.Shot("60_map_after_restart");
            yield return h.ClickElement(h.Ui.Map.NodeButton(2)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            Assert.That(h.Game.Level!.Seed, Is.EqualTo(seed2), "level 2 is the same board after the restart");
        }

        [UnityTest]
        public IEnumerator Retry_after_a_loss_plays_the_same_board_again()
        {
            yield return Boot();
            ulong seed = 987654321;
            h.App.Progress.Load(new SaveData { CurrentLevel = 15, CurrentSeed = seed });
            yield return h.StartLevel(15, seed);
            Assert.That(h.Game.Level!.Seed, Is.EqualTo(seed));
            string first = Fingerprint();
            yield return PlayWorst();
            if (h.Flow.Won) Assert.Inconclusive("won with the worst moves");
            Assert.That(h.Ui.Result.RetryButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(h.Ui.Result.NextButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(OnDisk().Stars.Get(15), Is.EqualTo(0), "a loss records no stars");
            yield return h.ClickElement(h.Ui.Result.RetryButton);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            Assert.That(h.Game.Level!.Seed, Is.EqualTo(seed), "retry reuses the seed");
            Assert.That(h.Session.MovesUsed, Is.EqualTo(0));
            Assert.That(Fingerprint(), Is.EqualTo(first), "the board after Retry is the same board");
            h.AssertViewMatches("after a retry");
            // and the seed survives a restart (the app flushes on pause; Load above did not trigger a save)
            h.App.Game.Flush();
            yield return h.Reboot();
            Assert.That(h.App.Progress.Data.CurrentSeed, Is.EqualTo(seed));
        }

        // ---- settings ----

        [UnityTest]
        public IEnumerator Settings_apply_live_and_are_saved()
        {
            yield return Boot();
            yield return h.ClickElement(h.Ui.Title.SettingsButton);
            yield return h.WaitForScreen(ScreenId.Settings);
            var s = h.Ui.Settings;
            s.SoundToggle.value = false;
            s.AdaptiveToggle.value = false;
            s.SpeedSlider.value = 2f;
            yield return PlayHarness.Frames(2);
            Assert.That(h.App.Live.Player.Sound, Is.False);
            Assert.That(h.App.Live.Player.Adaptive, Is.False);
            Assert.That(h.App.Live.Player.AnimationSpeed, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(h.App.Live.Timing.Speed, Is.EqualTo(2.0).Within(1e-9), "the animation timing follows live");
            Assert.That(s.SpeedLabel.text, Is.EqualTo("Animation speed x2.00"));
            var onDisk = new SaveService(new FileStorage(h.DataDir)).LoadSettings();
            Assert.That(onDisk, Is.EqualTo(h.App.Live.Player), "settings.json holds the new settings");
            s.SpeedSlider.value = 9f;
            yield return PlayHarness.Frames(2);
            Assert.That(h.App.Live.Player.AnimationSpeed, Is.EqualTo(3.0), "the slider is clamped to 3.0");

            yield return h.Reboot();
            Assert.That(h.App.Live.Player.Sound, Is.False);
            Assert.That(h.App.Live.Player.AnimationSpeed, Is.EqualTo(3.0));
            yield return h.ClickElement(h.Ui.Title.SettingsButton);
            yield return h.WaitForScreen(ScreenId.Settings);
            Assert.That(h.Ui.Settings.SoundToggle.value, Is.False, "the settings page shows what was saved");
            Assert.That(h.Ui.Settings.SpeedSlider.value, Is.EqualTo(3f));
        }

        // ---- layout ----

        [UnityTest]
        public IEnumerator Wide_and_tall_layouts_switch_on_resize_and_the_safe_area_insets_the_pages()
        {
            yield return Boot();
            Assert.That(h.Flow.Layout, Is.EqualTo(LayoutClass.Wide));
            Assert.That(h.Ui.Shell.ClassListContains("layout--wide"), Is.True);
            Assert.That(h.Ui.Shell.ClassListContains("layout--tall"), Is.False);

            h.Resize(1080, 2400, new SafeArea(0, 120, 0, 80));
            yield return PlayHarness.UiSettled();
            Assert.That(h.Flow.Layout, Is.EqualTo(LayoutClass.Tall));
            Assert.That(h.Ui.Shell.ClassListContains("layout--tall"), Is.True);
            Assert.That(h.Ui.Shell.ClassListContains("layout--wide"), Is.False);
            var ins = h.Ui.Insets;
            Assert.That(ins.Top, Is.GreaterThan(50f), "the top inset was applied in panel units");
            Assert.That(ins.Bottom, Is.GreaterThan(30f));
            Assert.That(h.Ui.Safe.worldBound.yMin, Is.EqualTo(ins.Top).Within(1f));
            Assert.That(h.Ui.Safe.worldBound.yMax, Is.EqualTo(h.Ui.Shell.worldBound.yMax - ins.Bottom).Within(1f));
            yield return h.Shot("61_title_tall_safe_area");

            // the board stays inside the safe area in play
            yield return h.StartLevel(1, 20241004);
            var slot = h.Hud.BoardSlot.worldBound;
            Assert.That(slot.yMin, Is.GreaterThanOrEqualTo(h.Ui.Safe.worldBound.yMin - 0.5f));
            Assert.That(slot.yMax, Is.LessThanOrEqualTo(h.Ui.Safe.worldBound.yMax + 0.5f));
            Assert.That(h.App.Rig.SlotPx.Y, Is.GreaterThanOrEqualTo(120f - 1f), "the fitted slot starts below the cutout in screen pixels");
            Assert.That(h.App.Rig.SlotPx.Bottom, Is.LessThanOrEqualTo(2400f - 80f + 1f));
            var tallSlot = h.App.Rig.SlotPx;

            h.Resize(1600, 900, new SafeArea(0, 0, 0, 0));
            yield return PlayHarness.UiSettled();
            Assert.That(h.Flow.Layout, Is.EqualTo(LayoutClass.Wide));
            var wideSlot = h.App.Rig.SlotPx;
            Assert.That(wideSlot.Width, Is.Not.EqualTo(tallSlot.Width).Within(10.0), "the board was refitted");
            Assert.That(h.Ui.Insets.Top, Is.EqualTo(0f).Within(0.1f), "no inset without a cutout");
            h.AssertViewMatches("after the layout switches");
        }

        [UnityTest]
        public IEnumerator Buttons_have_a_physical_minimum_size_on_a_touch_device_only()
        {
            yield return Boot(host: Phone);
            double mm = h.App.Dials.Input.MinTouchTargetMm;
            float panelW = h.Ui.Shell.worldBound.width;
            float expected = TouchSizing.ToPanelUnits(mm, 420f, panelW, h.Width);
            Assert.That(expected, Is.GreaterThan(30f));
            Assert.That(h.Ui.Sizing.MinPanelUnits, Is.EqualTo(expected).Within(0.5f));
            Assert.That(h.Ui.Title.PlayButton.resolvedStyle.minHeight.value, Is.EqualTo(expected).Within(0.5f));
            // new content is sized as well (map nodes)
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            Assert.That(h.Ui.Map.NodeButton(1)!.resolvedStyle.minHeight.value, Is.EqualTo(expected).Within(0.5f));
            Assert.That(TouchSizing.Pixels(9.0, 254f), Is.EqualTo(90f).Within(0.1f), "9 mm at 254 dpi is 90 px");
            yield return h.TearDown();

            yield return Boot(); // a desktop with a mouse
            Assert.That(h.Ui.Sizing.MinPanelUnits, Is.EqualTo(0f));
        }

        // ---- the map ----

        [UnityTest]
        public IEnumerator The_map_scrolls_keeps_a_window_and_starts_only_unlocked_levels()
        {
            yield return Boot();
            var stars = StarTable.Empty.With(1, 3).With(2, 1);
            h.App.Progress.Load(new SaveData { CurrentLevel = 40, Stars = stars });
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            var map = h.Ui.Map;
            Assert.That(map.Center, Is.EqualTo(40f), "the map opens on the current level");
            Assert.That(map.NodeButton(40), Is.Not.Null);
            Assert.That(map.BuiltLevels.Count, Is.LessThan(25), "only a window of levels is built");
            Assert.That(map.NodeButton(1), Is.Null);
            map.ScrollLevels(-30f);
            yield return PlayHarness.Frames(2);
            Assert.That(map.Center, Is.EqualTo(10f).Within(0.01f));
            Assert.That(map.NodeButton(10), Is.Not.Null, "the window followed the scroll");
            map.ScrollLevels(-100f);
            Assert.That(map.Center, Is.EqualTo(1f), "the path ends at level 1");
            yield return PlayHarness.Frames(2);
            Assert.That(map.NodeButton(1), Is.Not.Null);
            Assert.That(map.NodeElement(1)!.Query<StarElement>().ToList().Count(s => s.Earned), Is.EqualTo(3));
            map.ScrollLevels(500f);
            Assert.That(map.Center, Is.LessThanOrEqualTo(h.App.Models.Map.HighestShown), "the path ends at the locked teaser levels");
            yield return h.Shot("62_map_scrolled");
        }

        // ---- lifecycle ----

        [UnityTest]
        public IEnumerator A_phone_going_to_the_background_saves_and_shows_the_pause_menu()
        {
            yield return Boot(host: Phone);
            yield return h.StartLevel(1, 4242);
            Assert.That(File.Exists(Path.Combine(h.DataDir, SaveService.SettingsFile)), Is.False);
            h.App.Lifecycle.SimulatePause(true);
            yield return PlayHarness.Frames(2);
            Assert.That(File.Exists(Path.Combine(h.DataDir, SaveService.SettingsFile)), Is.True, "settings were flushed on pause");
            Assert.That(File.Exists(Path.Combine(h.DataDir, SaveService.SaveFile)), Is.True, "progress was flushed on pause");
            Assert.That(h.Flow.Paused, Is.True, "coming back shows the pause menu");
            Assert.That(h.Controller.Paused, Is.True);
            h.App.Lifecycle.SimulatePause(false);
            yield return PlayHarness.Frames(2);
            Assert.That(h.Flow.Paused, Is.True, "the player resumes by choice");
            yield return h.ClickElement(h.Hud.ResumeButton);
            Assert.That(h.Controller.Paused, Is.False);
        }

        [Test]
        public void A_three_finger_tap_is_three_fingers_down_and_up_quickly()
        {
            var d = new ThreeFingerTapDetector();
            Assert.That(d.Feed(1, 0.0), Is.False);
            Assert.That(d.Feed(3, 0.05), Is.False);
            Assert.That(d.Feed(0, 0.2), Is.True, "three fingers lifted within the tap time");
            Assert.That(d.Feed(2, 1.0), Is.False);
            Assert.That(d.Feed(0, 1.1), Is.False, "two fingers are not a three-finger tap");
            Assert.That(d.Feed(3, 2.0), Is.False);
            Assert.That(d.Feed(0, 3.5), Is.False, "a long press is not a tap");
        }

        // ---- the dev page ----

        [UnityTest]
        public IEnumerator The_dev_page_is_generated_from_the_dials_and_edits_them_live()
        {
            yield return Boot();
            yield return h.StartLevel(1, 20241004);
            var dev = h.Ui.Dev;
            Assert.That(dev, Is.Not.Null, "a dev build has the dev page");
            var dials = h.App.Dials;

            yield return h.Key(h.Keyboard.f1Key);
            yield return PlayHarness.UiSettled();
            Assert.That(h.Ui.IsDevShown, Is.True, "F1 opens the page");
            Assert.That(dev!.FieldCount, Is.EqualTo(dials.Metadata.Count), "every dial has a row");
            Assert.That(dev.Foldouts.Count, Is.EqualTo(dials.Metadata.Select(m => m.Section).Distinct().Count()), "one foldout per section");
            Assert.That(dev.LevelInfoLabel.text, Does.Contain("seed 20241004"));
            Assert.That(dev.IntSliderOf("animation.swap"), Is.Not.Null, "an integer dial has an integer slider");
            Assert.That(dev.IntSliderOf("camera.pitch_deg"), Is.Not.Null, "an integer dial has an integer slider");
            Assert.That(dev.SliderOf("camera.zoom_step"), Is.Not.Null, "a decimal dial has a decimal slider");
            Assert.That(dev.NumberOf("animation.swap")!.value, Is.EqualTo("180"));

            // animation.swap: typed into the number field, it reaches the running board's timing at once
            dev.NumberOf("animation.swap")!.value = "500";
            yield return PlayHarness.Frames(2);
            Assert.That(dials.GetInt("animation", "swap"), Is.EqualTo(500));
            Assert.That(h.Controller.Timing.SwapMs, Is.EqualTo(500.0).Within(1e-6), "the board animates with the new swap time");
            Assert.That(dev.RowOf("animation.swap")!.ClassListContains("dev-field--changed"), Is.True, "the changed flag is set");
            Assert.That(dev.MessageLabel.text, Does.Contain("animation.swap"));
            dev.NumberOf("animation.swap")!.value = "999999";
            yield return PlayHarness.Frames(2);
            Assert.That(dials.GetInt("animation", "swap"), Is.EqualTo(500), "an out-of-range value is rejected");
            Assert.That(dev.MessageLabel.text, Does.Contain("rejected"));

            // camera.pitch_deg: the slider moves the camera of the running board
            var camera = h.App.Rig.Board!;
            var before = camera.Pose.EulerDegrees.X;
            dev.IntSliderOf("camera.pitch_deg")!.value = 50;
            yield return PlayHarness.Frames(3);
            Assert.That(dials.GetInt("camera", "pitch_deg"), Is.EqualTo(50));
            Assert.That(camera.Pose.EulerDegrees.X, Is.Not.EqualTo(before).Within(1.0), "the camera pitch changed on screen");
            yield return h.Shot("63_dev_pitch_50");

            // a rule dial: the running session plays its next move with the new rule set
            dev.NumberOf("scoring.run3")!.value = "123";
            yield return PlayHarness.Frames(2);
            Assert.That(h.Session.Rules, Is.EqualTo(RuleSets.From(dials)), "the session's rules were rebuilt");

            // per-dial reset, Save, Reset all
            yield return h.ClickElement(dev.ResetOf("animation.swap")!);
            Assert.That(dials.GetInt("animation", "swap"), Is.EqualTo(180));
            yield return h.ClickElement(dev.SaveButton);
            string toml = File.ReadAllText(Path.Combine(h.DataDir, DialRegistry.DevOverridesFile));
            Assert.That(toml, Does.Contain("pitch_deg"));
            Assert.That(toml, Does.Contain("run3"));
            Assert.That(toml, Does.Not.Contain("swap"), "only changed dials are saved");
            yield return h.ClickElement(dev.ResetAllButton);
            Assert.That(h.App.Models.Dev.ChangedCount, Is.EqualTo(0));
            Assert.That(dials.GetInt("camera", "pitch_deg"), Is.EqualTo(25));

            // jump to level N
            dev.JumpField.value = 7;
            yield return h.ClickElement(dev.JumpButton);
            yield return h.WaitForScreen(ScreenId.Intro);
            Assert.That(h.Flow.DevOpen, Is.False, "the page closes on a jump");
            Assert.That(h.Game.Level!.Number, Is.EqualTo(7));

            // F1 again closes it
            yield return h.Key(h.Keyboard.f1Key);
            Assert.That(h.Flow.DevOpen, Is.True);
            yield return h.Key(h.Keyboard.f1Key);
            Assert.That(h.Flow.DevOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator Saved_dev_dials_are_loaded_by_the_next_boot()
        {
            yield return Boot();
            h.App.Dials.Set("camera", "pitch_deg", 40);
            Assert.That(h.App.Models.Dev.Save(), Is.True);
            yield return h.Reboot();
            Assert.That(h.App.Dials.GetInt("camera", "pitch_deg"), Is.EqualTo(40));
            Assert.That(h.App.Models.Dev.ChangedCount, Is.EqualTo(1));
        }
    }
}
