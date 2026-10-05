using System.Collections;
using Cascade.Gameplay;
using Cascade.Presentation.Shell;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Cascade.Tests.Play
{
    /// <summary>
    /// Screenshots of every page of the shell, wide and tall, into Builds/shots (looked at by a person; the assertions only check that
    /// each page is the one the flow says). The pages are reached the way a player reaches them: through buttons and the flow.
    /// </summary>
    public class ShellShots
    {
        PlayHarness h = null!;

        [UnityTearDown]
        public IEnumerator TearDown() => h.TearDown();

        /// <summary>A save with the frontier at 14 and a few stars on the way, so the map looks lived in.</summary>
        static SaveData SomeProgress()
        {
            var stars = StarTable.Empty;
            int[] s = { 3, 2, 3, 1, 3, 2, 2, 3, 1, 3, 2, 3, 3 };
            for (int i = 0; i < s.Length; i++) stars = stars.With(i + 1, s[i]);
            return new SaveData { CurrentLevel = 14, Stars = stars };
        }

        /// <summary>Plays the current level with the session's best moves (direct requests, so it is quick) until it ends, and waits for the result page.</summary>
        IEnumerator FinishLevel()
        {
            while (h.Session.IsPlaying)
            {
                var moves = h.Session.LegalMoves;
                var best = moves[0];
                int high = int.MinValue;
                foreach (var m in moves)
                {
                    var copy = h.Session.Copy();
                    copy.TrySwap(m.A, m.B);
                    int gain = copy.Score + copy.TilesCollected * 1000;
                    if (gain > high) { high = gain; best = m; }
                }
                h.Controller.RequestSwap(best.A, best.B);
                yield return h.Settled();
                yield return PlayHarness.Frames(1);
            }
            yield return h.WaitForResult();
        }

        /// <summary>Plays the last legal move again and again: the level is lost unless it is very easy.</summary>
        IEnumerator LoseLevel()
        {
            while (h.Session.IsPlaying)
            {
                var moves = h.Session.LegalMoves;
                Assert.That(moves.Count, Is.GreaterThan(0));
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

        IEnumerator Tour(int w, int hgt, string tag)
        {
            h = new PlayHarness();
            yield return h.SetUp(w, hgt, gated: true);
            h.App.Progress.Load(SomeProgress());
            h.Gate!.Hold();
            h.App.Live.Update(h.App.Live.Player with { AnimationSpeed = 3.0 });
            yield return PlayHarness.UiSettled();
            Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Title));
            yield return h.Shot("30_title_" + tag);

            yield return h.ClickElement(h.Ui.Title.SettingsButton);
            yield return h.WaitForScreen(ScreenId.Settings);
            yield return h.Shot("31_settings_" + tag);
            yield return h.ClickElement(h.Ui.Settings.BackButton);
            yield return h.WaitForScreen(ScreenId.Title);

            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            yield return h.Shot("32_map_" + tag);

            // a normal level: Loading (held), then the intro
            yield return h.ClickElement(h.Ui.Map.NodeButton(12)!);
            yield return h.WaitForScreen(ScreenId.Loading);
            yield return h.Shot("33_loading_" + tag);
            h.Gate.Release();
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.Shot("34_intro_" + tag);

            // an intro level
            yield return h.ClickElement(h.Ui.Intro.BackButton);
            yield return h.WaitForScreen(ScreenId.Map);
            yield return h.ClickElement(h.Ui.Map.NodeButton(11)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.Shot("35_intro_new_mechanic_" + tag);

            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            yield return h.Shot("36_play_" + tag);

            yield return h.ClickElement(h.Hud.PauseButton);
            yield return PlayHarness.UiSettled();
            yield return h.Shot("37_pause_" + tag);
            yield return h.ClickElement(h.Hud.ResumeButton);

            // dev page over the play screen
            yield return h.Key(h.Keyboard.f1Key);
            yield return PlayHarness.UiSettled();
            yield return h.Shot("38_dev_" + tag);
            yield return h.Key(h.Keyboard.f1Key);
            yield return PlayHarness.UiSettled();

            // win, with a boost so that all three stars are certain
            h.App.Dials.Set("scoring", "run3", 400);
            yield return FinishLevel();
            yield return PlayHarness.Seconds(0.9f);
            Assert.That(h.Flow.Won, Is.True);
            yield return h.Shot("39_result_win_3stars_" + tag);

            // a lost level (the frontier is 15 now): Retry is offered
            yield return h.ClickElement(h.Ui.Result.MapButton);
            yield return h.WaitForScreen(ScreenId.Map);
            h.App.Dials.ResetAll();
            yield return h.Shot("40_map_after_win_" + tag);
            yield return h.ClickElement(h.Ui.Map.NodeButton(h.App.Progress.CurrentLevel)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            yield return LoseLevel();
            yield return PlayHarness.Seconds(0.9f);
            if (h.Flow.Won) Assert.Inconclusive("the level was won with the worst moves; no lose screenshot");
            yield return h.Shot("41_result_lose_" + tag);
        }

        [UnityTest]
        public IEnumerator Wide_pages() => Tour(1280, 800, "wide");

        [UnityTest]
        public IEnumerator Tall_pages() => Tour(1080, 1920, "tall");

        [UnityTest]
        public IEnumerator Ultra_wide_pages() => Squeeze(2560, 1080, "ultrawide");

        [UnityTest]
        public IEnumerator Phone_portrait_with_cutout_pages() => Squeeze(1080, 2400, "phone");

        /// <summary>The pages that carry the most content, at an unusual window shape.</summary>
        IEnumerator Squeeze(int w, int hgt, string tag)
        {
            h = new PlayHarness();
            yield return h.SetUp(w, hgt, gated: false);
            h.App.Progress.Load(SomeProgress());
            if (w < hgt)
                h.Resize(w, hgt, new Cascade.Platform.Abstractions.SafeArea(0, 110, 0, 70));
            yield return PlayHarness.UiSettled();
            yield return h.Shot("50_title_" + tag);
            yield return h.ClickElement(h.Ui.Title.PlayButton);
            yield return h.WaitForScreen(ScreenId.Map);
            yield return h.ClickElement(h.Ui.Map.NodeButton(11)!);
            yield return h.WaitForScreen(ScreenId.Intro);
            yield return h.Shot("51_intro_" + tag);
            yield return h.ClickElement(h.Ui.Intro.PlayButton);
            yield return h.WaitForScreen(ScreenId.Play);
            yield return h.Shot("52_play_" + tag);
        }
    }
}
