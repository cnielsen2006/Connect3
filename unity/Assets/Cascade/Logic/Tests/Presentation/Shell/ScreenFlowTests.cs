using System.Collections.Generic;
using Cascade.Platform.Abstractions;
using Cascade.Presentation.Shell;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 2.1 / 12.2 / 13.5, Presentation/ScreenFlow: every (state, command) pair, row by row.</summary>
    public class ScreenFlowTests
    {
        static readonly HostInfo Desktop = new HostInfo(PlatformKind.Desktop, true, true, false, true, 96f);
        static readonly HostInfo DesktopRelease = new HostInfo(PlatformKind.Desktop, true, true, false, false, 96f);
        static readonly HostInfo Android = new HostInfo(PlatformKind.Android, false, false, true, false, 420f);
        static readonly HostInfo AndroidDev = new HostInfo(PlatformKind.Android, false, false, true, true, 420f);
        static readonly HostInfo Web = new HostInfo(PlatformKind.Web, true, true, false, false, 96f);

        /// <summary>Levels 1 to 5 are unlocked.</summary>
        static ScreenFlow Make(HostInfo? host = null) => new ScreenFlow(host ?? Desktop, n => n <= 5);

        // ---- state vocabulary: Title Map Loading Intro Play Paused ResultWin ResultLose Settings

        static readonly string[] States =
            { "Title", "Map", "Loading", "Intro", "Play", "Paused", "ResultWin", "ResultLose", "Settings" };

        static ScreenFlow At(string state, HostInfo? host = null)
        {
            var f = Make(host);
            switch (state)
            {
                case "Title": break;
                case "Settings": Assert.That(f.OpenSettings()); break;
                case "Map": Assert.That(f.Play()); break;
                case "Loading": f.Play(); Assert.That(f.SelectLevel(3)); break;
                case "Intro": f.Play(); f.SelectLevel(3); Assert.That(f.LevelReady()); break;
                case "Play": f.Play(); f.SelectLevel(3); f.LevelReady(); Assert.That(f.StartLevel()); break;
                case "Paused": f.Play(); f.SelectLevel(3); f.LevelReady(); f.StartLevel(); Assert.That(f.Pause()); break;
                case "ResultWin": f.Play(); f.SelectLevel(3); f.LevelReady(); f.StartLevel(); Assert.That(f.LevelEnded(true)); break;
                case "ResultLose": f.Play(); f.SelectLevel(3); f.LevelReady(); f.StartLevel(); Assert.That(f.LevelEnded(false)); break;
                default: Assert.Fail("unknown state " + state); break;
            }
            Assert.That(Describe(f), Is.EqualTo(state));
            return f;
        }

        static string Describe(ScreenFlow f)
        {
            if (f.Current == ScreenId.Play && f.Paused) return "Paused";
            if (f.Current == ScreenId.Result) return f.Won ? "ResultWin" : "ResultLose";
            return f.Current.ToString();
        }

        static readonly string[] Commands =
        {
            "Play", "OpenSettings", "Quit", "Back", "Select3", "Select9", "Select0", "LevelReady", "LoadFailed", "StartLevel",
            "Pause", "Resume", "TogglePause", "EndWin", "EndLose", "Retry", "Next", "ToMap", "ToTitle",
        };

        static bool Exec(ScreenFlow f, string command)
        {
            switch (command)
            {
                case "Play": return f.Play();
                case "OpenSettings": return f.OpenSettings();
                case "Quit": return f.Quit();
                case "Back": return f.BackPressed();
                case "Select3": return f.SelectLevel(3);
                case "Select9": return f.SelectLevel(9); // locked
                case "Select0": return f.SelectLevel(0);
                case "LevelReady": return f.LevelReady();
                case "LoadFailed": return f.LoadFailed();
                case "StartLevel": return f.StartLevel();
                case "Pause": return f.Pause();
                case "Resume": return f.Resume();
                case "TogglePause": return f.TogglePause();
                case "EndWin": return f.LevelEnded(true);
                case "EndLose": return f.LevelEnded(false);
                case "Retry": return f.Retry();
                case "Next": return f.Next();
                case "ToMap": return f.ToMap();
                case "ToTitle": return f.ToTitle();
                default: throw new KeyNotFoundException(command);
            }
        }

        /// <summary>The only accepted pairs on desktop (a dev build); everything else must be refused without a change.</summary>
        static readonly Dictionary<(string, string), string> Accepted = new Dictionary<(string, string), string>
        {
            [("Title", "Play")] = "Map",
            [("Title", "OpenSettings")] = "Settings",
            [("Title", "Quit")] = "Title", // asks to leave the app, the page stays
            [("Map", "Select3")] = "Loading",
            [("Map", "ToTitle")] = "Title",
            [("Map", "Back")] = "Title",
            [("Loading", "LevelReady")] = "Intro",
            [("Loading", "LoadFailed")] = "Map",
            [("Loading", "ToMap")] = "Map",
            [("Loading", "Back")] = "Map",
            [("Intro", "StartLevel")] = "Play",
            [("Intro", "ToMap")] = "Map",
            [("Intro", "Back")] = "Map",
            [("Play", "Pause")] = "Paused",
            [("Play", "TogglePause")] = "Paused",
            [("Play", "Back")] = "Paused",
            [("Play", "EndWin")] = "ResultWin",
            [("Play", "EndLose")] = "ResultLose",
            [("Paused", "Resume")] = "Play",
            [("Paused", "TogglePause")] = "Play",
            [("Paused", "Back")] = "Play",
            [("Paused", "ToMap")] = "Map",
            [("Paused", "EndWin")] = "ResultWin",
            [("Paused", "EndLose")] = "ResultLose",
            [("ResultWin", "Next")] = "Loading",
            [("ResultWin", "ToMap")] = "Map",
            [("ResultWin", "Back")] = "Map",
            [("ResultLose", "Retry")] = "Loading",
            [("ResultLose", "ToMap")] = "Map",
            [("ResultLose", "Back")] = "Map",
            [("Settings", "ToTitle")] = "Title",
            [("Settings", "Back")] = "Title",
        };

        public static IEnumerable<TestCaseData> Matrix()
        {
            foreach (var state in States)
                foreach (var command in Commands)
                {
                    bool ok = Accepted.TryGetValue((state, command), out var to);
                    yield return new TestCaseData(state, command, ok, ok ? to : state)
                        .SetName($"{state}_{command}_{(ok ? "goes_to_" + to : "is_refused")}");
                }
        }

        [TestCaseSource(nameof(Matrix))]
        public void Every_state_and_command_pair(string state, string command, bool accepted, string expected)
        {
            var f = At(state);
            int screenChanges = 0;
            var pageBefore = f.Current;
            f.ScreenChanged += (_, __) => screenChanges++;
            bool result = Exec(f, command);
            Assert.That(result, Is.EqualTo(accepted));
            Assert.That(Describe(f), Is.EqualTo(expected));
            Assert.That(screenChanges > 0, Is.EqualTo(f.Current != pageBefore), "ScreenChanged fires exactly when the page changes");
        }

        [Test]
        public void The_matrix_covers_every_state_and_command()
        {
            Assert.That(States.Length * Commands.Length, Is.EqualTo(9 * 19));
            foreach (var (state, command) in Accepted.Keys)
            {
                Assert.That(States, Does.Contain(state));
                Assert.That(Commands, Does.Contain(command));
            }
        }

        // ---- Back on the title, per platform

        [Test]
        public void Back_on_the_title_does_nothing_on_desktop_which_has_a_quit_button()
        {
            var f = Make(Desktop);
            int leave = 0;
            f.LeaveAppRequested += () => leave++;
            Assert.That(f.QuitAvailable);
            Assert.That(f.BackPressed(), Is.False);
            Assert.That(leave, Is.EqualTo(0));
            Assert.That(f.Quit());
            Assert.That(leave, Is.EqualTo(1));
        }

        [Test]
        public void Back_on_the_title_asks_to_leave_the_app_on_android_which_has_no_quit_button()
        {
            var f = Make(Android);
            int leave = 0;
            f.LeaveAppRequested += () => leave++;
            Assert.That(f.QuitAvailable, Is.False);
            Assert.That(f.Quit(), Is.False, "no Quit command on Android");
            Assert.That(leave, Is.EqualTo(0));
            Assert.That(f.BackPressed());
            Assert.That(leave, Is.EqualTo(1));
            Assert.That(f.Current, Is.EqualTo(ScreenId.Title));
        }

        [Test]
        public void A_browser_tab_has_no_quit_button_and_back_on_the_title_does_nothing()
        {
            var f = Make(Web);
            int leave = 0;
            f.LeaveAppRequested += () => leave++;
            Assert.That(f.QuitAvailable, Is.False);
            Assert.That(f.Quit(), Is.False, "a tab cannot quit itself");
            Assert.That(f.BackPressed(), Is.False);
            Assert.That(leave, Is.EqualTo(0));
            Assert.That(f.Current, Is.EqualTo(ScreenId.Title));
        }

        [Test]
        public void Back_elsewhere_never_leaves_the_app()
        {
            foreach (var host in new[] { Desktop, Android, Web })
                foreach (var state in new[] { "Map", "Loading", "Intro", "Play", "Paused", "ResultWin", "Settings" })
                {
                    var f = At(state, host);
                    int leave = 0;
                    f.LeaveAppRequested += () => leave++;
                    f.BackPressed();
                    Assert.That(leave, Is.EqualTo(0), host.Kind + " " + state);
                }
        }

        [Test]
        public void Back_during_play_pauses_and_back_again_resumes_like_esc()
        {
            var f = At("Play");
            Assert.That(f.InputEnabled);
            f.BackPressed();
            Assert.That(f.Paused);
            Assert.That(f.InputEnabled, Is.False);
            f.BackPressed();
            Assert.That(f.Paused, Is.False);
            Assert.That(f.InputEnabled);
            Assert.That(f.Current, Is.EqualTo(ScreenId.Play));
        }

        // ---- level requests

        [Test]
        public void Select_asks_for_the_level_and_refuses_locked_levels()
        {
            var f = At("Map");
            var requests = new List<LevelRequest>();
            f.LevelRequested += requests.Add;
            Assert.That(f.SelectLevel(9), Is.False, "level 9 is locked");
            Assert.That(f.SelectLevel(3));
            Assert.That(requests, Is.EqualTo(new[] { new LevelRequest(3, LevelRequestKind.Select) }));
            Assert.That(f.LevelNumber, Is.EqualTo(3));
        }

        [Test]
        public void Next_and_retry_ask_for_the_following_or_the_same_level()
        {
            var requests = new List<LevelRequest>();
            var win = At("ResultWin");
            win.LevelRequested += requests.Add;
            Assert.That(win.Next());
            var lose = At("ResultLose");
            lose.LevelRequested += requests.Add;
            Assert.That(lose.Retry());
            Assert.That(requests, Is.EqualTo(new[]
            {
                new LevelRequest(4, LevelRequestKind.Next), new LevelRequest(3, LevelRequestKind.Retry),
            }));
        }

        [Test]
        public void The_page_is_already_Loading_when_the_request_is_raised_so_a_synchronous_answer_works()
        {
            var f = At("Map");
            var seen = new List<string>();
            f.ScreenChanged += (from, to) => seen.Add(from + ">" + to);
            f.LevelRequested += _ => f.LevelReady(); // answers at once, inside the event
            Assert.That(f.SelectLevel(2));
            Assert.That(f.Current, Is.EqualTo(ScreenId.Intro));
            Assert.That(seen, Is.EqualTo(new[] { "Map>Loading", "Loading>Intro" }));
        }

        // ---- dev overlay

        [Test]
        public void Dev_overlay_opens_and_closes_in_a_dev_build_only()
        {
            var dev = Make(Desktop);
            Assert.That(dev.DevAvailable);
            Assert.That(dev.OpenDev());
            Assert.That(dev.DevOpen);
            Assert.That(dev.OpenDev(), Is.False, "already open");
            Assert.That(dev.CloseDev());
            Assert.That(dev.CloseDev(), Is.False);
            Assert.That(dev.ToggleDev());
            Assert.That(dev.DevOpen);
            Assert.That(dev.ToggleDev());
            Assert.That(dev.DevOpen, Is.False);

            foreach (var host in new[] { DesktopRelease, Android })
            {
                var rel = Make(host);
                Assert.That(rel.DevAvailable, Is.False);
                Assert.That(rel.OpenDev(), Is.False);
                Assert.That(rel.ToggleDev(), Is.False);
                Assert.That(rel.DevJump(5), Is.False);
                Assert.That(rel.DevOpen, Is.False);
            }
            Assert.That(Make(AndroidDev).OpenDev(), "a development build on a phone");
        }

        [Test]
        public void Back_closes_the_dev_overlay_before_anything_else()
        {
            var f = At("Play");
            f.OpenDev();
            Assert.That(f.InputEnabled, Is.False, "the overlay blocks the board");
            Assert.That(f.BackPressed());
            Assert.That(f.DevOpen, Is.False);
            Assert.That(f.Paused, Is.False, "that Back did not also pause");
            Assert.That(f.Current, Is.EqualTo(ScreenId.Play));
        }

        [Test]
        public void Dev_overlay_works_on_every_page()
        {
            foreach (var state in States)
            {
                var f = At(state);
                Assert.That(f.OpenDev(), state);
                Assert.That(f.CloseDev(), state);
                Assert.That(Describe(f), Is.EqualTo(state));
            }
        }

        [Test]
        public void Dev_jump_closes_the_overlay_leaves_any_page_and_ignores_the_unlock_rule()
        {
            foreach (var state in States)
            {
                var f = At(state);
                var requests = new List<LevelRequest>();
                f.LevelRequested += requests.Add;
                f.OpenDev();
                Assert.That(f.DevJump(40), state);
                Assert.That(f.DevOpen, Is.False, state);
                Assert.That(f.Current, Is.EqualTo(ScreenId.Loading), state);
                Assert.That(f.Paused, Is.False, state);
                Assert.That(requests, Is.EqualTo(new[] { new LevelRequest(40, LevelRequestKind.DevJump) }), state);
            }
            Assert.That(Make(Desktop).DevJump(0), Is.False);
        }

        // ---- layout

        [TestCase(1920, 1080, LayoutClass.Wide)]
        [TestCase(1080, 1920, LayoutClass.Tall)]
        [TestCase(1024, 768, LayoutClass.Wide)]
        [TestCase(768, 1024, LayoutClass.Tall)]
        [TestCase(1001, 1000, LayoutClass.Wide)]
        [TestCase(1000, 1001, LayoutClass.Tall)]
        [TestCase(1000, 1000, LayoutClass.Tall)] // exact tie goes to tall
        [TestCase(0, 0, LayoutClass.Tall)]
        [TestCase(-5, 100, LayoutClass.Tall)]
        public void Layout_class_is_the_one_closest_to_the_screen_shape(int w, int h, LayoutClass expected)
        {
            Assert.That(ScreenFlow.LayoutFor(w, h), Is.EqualTo(expected));
            var f = Make();
            f.SetScreen(w, h);
            Assert.That(f.Layout, Is.EqualTo(expected));
            Assert.That(f.LayoutCssClass, Is.EqualTo(expected == LayoutClass.Wide ? "layout--wide" : "layout--tall"));
        }

        [Test]
        public void Layout_notifications_fire_only_when_the_class_changes()
        {
            var f = Make();
            var layouts = new List<LayoutClass>();
            int changed = 0;
            f.LayoutChanged += layouts.Add;
            f.Changed += () => changed++;
            f.SetScreen(1920, 1080); // wide already
            Assert.That(layouts, Is.Empty);
            f.SetScreen(new ScreenInfo(1080, 2400, new SafeArea(0, 0, 0, 0)));
            f.SetScreen(1080, 2300); // still tall
            f.SetScreen(2400, 1080); // rotated
            Assert.That(layouts, Is.EqualTo(new[] { LayoutClass.Tall, LayoutClass.Wide }));
            Assert.That(changed, Is.EqualTo(2));
        }

        // ---- notifications

        [Test]
        public void ScreenChanged_reports_from_and_to_and_overlays_only_raise_Changed()
        {
            var f = Make();
            var pages = new List<string>();
            int changed = 0;
            f.ScreenChanged += (a, b) => pages.Add(a + ">" + b);
            f.Changed += () => changed++;
            f.Play();
            f.SelectLevel(1);
            f.LevelReady();
            f.StartLevel();
            int before = pages.Count;
            f.Pause();
            f.OpenDev();
            Assert.That(pages.Count, Is.EqualTo(before), "pause and dev are overlays, not pages");
            Assert.That(pages, Is.EqualTo(new[] { "Title>Map", "Map>Loading", "Loading>Intro", "Intro>Play" }));
            Assert.That(changed, Is.EqualTo(6));
        }

        [Test]
        public void Pause_belongs_to_one_play_session()
        {
            var f = At("Paused");
            Assert.That(f.ToMap());
            Assert.That(f.Paused, Is.False);
            f.SelectLevel(2); f.LevelReady(); f.StartLevel();
            Assert.That(f.Paused, Is.False, "a new level does not start paused");
            Assert.That(f.InputEnabled);
        }
    }
}
