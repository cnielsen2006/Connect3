using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Generation;
using Cascade.Platform.Abstractions;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Generation
{
    /// <summary>Tests (TRD §9.1 Generation/Curve, §6).</summary>
    public class CurveProfileTests
    {
        static Curve C(CurveShape shape, params (double, double)[] pts) => new Curve(shape, pts);

        // -- curve shapes ----------------------------------------------------------------------

        [Test]
        public void Linear_ease_out_step_values_and_constant_outside()
        {
            var pts = new[] { (10.0, 0.0), (20.0, 1.0), (30.0, 3.0) };
            var lin = C(CurveShape.Linear, pts);
            var ease = C(CurveShape.EaseOut, pts);
            var step = C(CurveShape.Step, pts);
            Assert.That(new[] { lin.At(5), ease.At(5), step.At(5) }, Is.All.EqualTo(0.0)); // before the first point
            Assert.That(new[] { lin.At(99), ease.At(99), step.At(99) }, Is.All.EqualTo(3.0)); // after the last
            Assert.That(lin.At(15), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(ease.At(15), Is.EqualTo(0.75).Within(1e-12)); // 1 - (1 - 0.5)^2
            Assert.That(ease.At(25), Is.EqualTo(1.0 + 2.0 * 0.75).Within(1e-12));
            Assert.That(new[] { lin.At(20), ease.At(20), step.At(20) }, Is.All.EqualTo(1.0)); // control points are hit exactly
            Assert.That(step.At(19), Is.EqualTo(0.0));
            Assert.That(step.At(20), Is.EqualTo(1.0));
            Assert.That(step.At(29), Is.EqualTo(1.0));
        }

        [Test]
        public void Ease_out_rises_faster_than_linear()
        {
            var pts = new[] { (1.0, 0.0), (101.0, 1.0) };
            for (int n = 2; n < 101; n++)
                Assert.That(C(CurveShape.EaseOut, pts).At(n), Is.GreaterThan(C(CurveShape.Linear, pts).At(n)), n.ToString());
        }

        [Test]
        public void Bad_curves_are_rejected()
        {
            Assert.Throws<ArgumentException>(() => C(CurveShape.Linear)); // no points
            Assert.Throws<ArgumentException>(() => C(CurveShape.Linear, (5, 1), (5, 2))); // not increasing
            Assert.Throws<ArgumentException>(() => C(CurveShape.Step, (9, 1), (2, 2)));
            Assert.That(Curve.TryParseShape("wobbly", out _), Is.False);
            Assert.That(Curve.TryParseShape("ease_out", out var shape) && shape == CurveShape.EaseOut);
        }

        // -- pressure / release ----------------------------------------------------------------

        static Func<int, double> Make(Curve? trend = null, Curve? amplitude = null)
        {
            trend ??= C(CurveShape.Linear, (1, 0.2), (150, 0.2));
            amplitude ??= C(CurveShape.Linear, (1, 0.1), (150, 0.1));
            return n => Difficulty.At(n, trend, amplitude, 7, 1.6, 0.6);
        }

        [Test]
        public void Wave_formula_matches_the_prd()
        {
            Assert.That(Difficulty.Wave(0, 7, 1.6, 0.6), Is.EqualTo(-0.6));
            Assert.That(Difficulty.Wave(1, 7, 1.6, 0.6), Is.EqualTo(0.0));
            Assert.That(Difficulty.Wave(6, 7, 1.6, 0.6), Is.EqualTo(1.0)); // the pinch
            Assert.That(Difficulty.Wave(3, 7, 1.6, 0.6), Is.EqualTo(Math.Pow(2.0 / 5.0, 1.6)).Within(1e-12));
        }

        [Test]
        public void Pinch_levels_are_multiples_of_P_and_releases_follow()
        {
            var d = Make();
            foreach (int n in new[] { 7, 14, 21, 70 })
            {
                Assert.That(Difficulty.IsPinch(n, 7) && Difficulty.IsRelease(n + 1, 7));
                Assert.That(d(n), Is.EqualTo(0.3).Within(1e-12)); // trend + amplitude * 1
                Assert.That(d(n + 1), Is.EqualTo(0.2 - 0.06).Within(1e-12)); // trend - amplitude * R
                Assert.That(d(n), Is.GreaterThan(d(n - 1))); // pressure builds
                Assert.That(d(n - 1), Is.GreaterThan(d(n - 2)));
                Assert.That(d(n + 1), Is.LessThan(d(n - 1))); // release is the easiest of the cycle
            }
            Assert.That(!Difficulty.IsPinch(8, 7) && !Difficulty.IsRelease(7, 7) && !Difficulty.IsRelease(1, 7));
        }

        [Test]
        public void Difficulty_is_clamped_to_the_unit_interval()
        {
            var hi = Make(C(CurveShape.Linear, (1, 0.95)), C(CurveShape.Linear, (1, 0.5)));
            var lo = Make(C(CurveShape.Linear, (1, 0.0)), C(CurveShape.Linear, (1, 0.5)));
            for (int n = 1; n < 60; n++)
            {
                Assert.That(hi(n), Is.InRange(0.0, 1.0));
                Assert.That(lo(n), Is.InRange(0.0, 1.0));
            }
            Assert.That(hi(7), Is.EqualTo(1.0));
            Assert.That(lo(8), Is.EqualTo(0.0));
        }

        // -- profile ---------------------------------------------------------------------------

        static Profile Shipped() => TestDials.Profile();

        [Test]
        public void Default_profile_matches_the_docs()
        {
            var profile = Shipped();
            Assert.That(profile.Version, Is.EqualTo(2));
            Assert.That(profile.CycleLength, Is.EqualTo(7));
            LevelParams p7 = profile.Evaluate(7), p8 = profile.Evaluate(8);
            Assert.That(p7.IsPinch && !p7.IsRelease && p8.IsRelease);
            Assert.That(p7.Difficulty, Is.GreaterThan(p8.Difficulty));
            Assert.That(p7.TargetWinRate, Is.EqualTo(0.97 + (0.35 - 0.97) * p7.Difficulty).Within(1e-12));
            Assert.That(p7.TargetWinRate, Is.InRange(0.35, 0.97));
        }

        [Test]
        public void Unlocks_and_bands()
        {
            var profile = Shipped();
            Assert.That(profile.Evaluate(10).Specials.Count, Is.EqualTo(0));
            Assert.That(profile.Evaluate(10).ForceGoal, Is.Null);
            var p = profile.Evaluate(11);
            Assert.That(p.IsIntro);
            Assert.That(p.Intro, Is.EqualTo("Collect goals and Line pieces"));
            Assert.That(p.ForceGoal, Is.EqualTo("collect"));
            Assert.That(p.Specials.ToArray(), Is.EqualTo(new[] { "Line" }));
            Assert.That(p.BoardSize, Is.EqualTo(8));
            Assert.That(p.Colors, Is.EqualTo(4));
            Assert.That(p.Difficulty, Is.EqualTo(0.1));
            Assert.That(profile.Evaluate(20).Intro, Is.Null);
            Assert.That(profile.Evaluate(21).Intro, Is.EqualTo("Bomb pieces"));
            Assert.That(profile.Evaluate(21).Colors, Is.EqualTo(5));
            Assert.That(!profile.Evaluate(35).Shaped);
            var p36 = profile.Evaluate(36);
            Assert.That(p36.Shaped && p36.ForceHoles);
            Assert.That(p36.Intro, Is.EqualTo("Shaped boards"));
            Assert.That(profile.Evaluate(51).Specials.ToArray(), Is.EqualTo(new[] { "Line", "Bomb", "Color" }));
            Assert.That(profile.Evaluate(51).Colors, Is.EqualTo(6));
            Assert.That(profile.Evaluate(76).Colors, Is.EqualTo(7));
            var p76 = profile.Evaluate(76);
            Assert.That(p76.ForceGoal, Is.EqualTo("combo"));
            Assert.That(p76.GoalPool.Contains(("combo", 0.3)));
            Assert.That(profile.Evaluate(5).GoalPool.Select(k => k.Kind).ToArray(), Is.EqualTo(new[] { "score" }));
            Assert.That(profile.Evaluate(30).GoalPool.Select(k => k.Kind).ToArray(), Is.EqualTo(new[] { "score", "collect" }));
        }

        [Test]
        public void Unlock_levels_and_intro_unlocks_are_queryable()
        {
            var profile = Shipped();
            Assert.That(profile.UnlockLevel("holes"), Is.EqualTo(36));
            Assert.That(profile.IntroUnlocks(11), Is.EqualTo(new[] { "goal_collect", "special_line" }));
            Assert.That(profile.IntroUnlocks(12), Is.Empty);
            Assert.Throws<ArgumentException>(() => profile.UnlockLevel("nope"));
        }

        [Test]
        public void Evaluate_rejects_level_zero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Shipped().Evaluate(0));
        }

        static string DefaultText() => TestDials.ProfileText().Replace("\r\n", "\n");

        [Test]
        public void Invalid_toml_is_rejected_with_a_clear_error()
        {
            var e = Assert.Throws<ProfileException>(() => Profile.FromText("[meta\nversion = 2"));
            StringAssert.Contains("invalid TOML", e!.Message);
        }

        [Test]
        public void Missing_shipped_file_surfaces_the_data_source_error()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Profile.Load(new DictionaryDataSource()));
        }

        [TestCase("version = 2", "version = \"two\"", @"\[meta\].version")]
        [TestCase("cycle_length = 7", "cycle_length = 2", "cycle_length")]
        [TestCase("ramp_power = 1.6", "ramp_power = 0", "ramp_power")]
        [TestCase("easiest_win_rate = 0.97", "easiest_win_rate = 1.5", "easiest_win_rate")]
        [TestCase("curve = \"step\"\npoints = [[1, 4]", "curve = \"bumpy\"\npoints = [[1, 4]", "unknown curve")]
        [TestCase("points = [[1, 7], [11, 8]]", "points = [[11, 7], [1, 8]]", "strictly increasing")]
        [TestCase("points = [[1, 7], [11, 8]]", "points = [[1, 7, 3]]", "expected")]
        [TestCase("holes = 36", "holes = 0", @"unlocks\].holes")]
        [TestCase("holes = 36", "holes = 36\nflying = 2", "unknown unlock")]
        [TestCase("11 = { score = 0.4, collect = 0.6 }", "11 = { score = 0, collect = 0 }", "all be zero")]
        [TestCase("11 = { score = 0.4, collect = 0.6 }", "11 = { shout = 1.0 }", "unknown goal kind")]
        [TestCase("[params.goal_amount]", "[params.goal_amount_x]", @"params.goal_amount\]")]
        [TestCase("holes = 36", "holes = 36.5", @"unlocks\].holes")]
        [TestCase("11 = { score = 0.4, collect = 0.6 }", "11 = { score = -1, collect = 0.6 }", "must be >= 0")]
        [TestCase("11 = { score = 0.4, collect = 0.6 }", "x11 = { score = 0.4, collect = 0.6 }", "level number")]
        [TestCase("trend = { curve = \"ease_out\", points = [[1, 0.05], [150, 0.75]] }", "trend = 5", "trend")]
        public void Invalid_profiles_name_the_problem(string old, string replacement, string pattern)
        {
            var text = DefaultText();
            Assert.That(text, Does.Contain(old), old);
            var bad = text.Replace(old, replacement);
            var e = Assert.Throws<ProfileException>(() => Profile.FromText(bad));
            Assert.That(e!.Message, Does.Match(pattern));
        }

        // -- hot reload ------------------------------------------------------------------------

        [Test]
        public void ReloadIfChanged_picks_up_edits_and_survives_bad_ones()
        {
            var storage = new MemoryStorage();
            var data = new DictionaryDataSource().With(Profile.DataKey, DefaultText());
            var prof = Profile.Load(data, storage);
            Assert.That(prof.ReloadIfChanged(), Is.False); // nothing changed
            var edited = DefaultText().Replace("release_depth = 0.6", "release_depth = 0.2");
            storage.PutText(Profile.DevOverrideFile, edited);
            Assert.That(prof.ReloadIfChanged(), Is.True);
            Assert.That(prof.ReleaseDepth, Is.EqualTo(0.2));
            storage.PutText(Profile.DevOverrideFile, "[meta\n");
            Assert.That(prof.ReloadIfChanged(), Is.False);
            Assert.That(prof.LastError, Is.Not.Null.And.Contains("invalid TOML"));
            Assert.That(prof.ReleaseDepth, Is.EqualTo(0.2)); // old profile kept
            Assert.That(prof.ReloadIfChanged(), Is.False); // the same bad text is reported once
            storage.PutText(Profile.DevOverrideFile, edited);
            Assert.That(prof.ReloadIfChanged(), Is.True);
            Assert.That(prof.LastError, Is.Null);
            storage.Delete(Profile.DevOverrideFile); // dropping the override returns to the shipped file
            Assert.That(prof.ReloadIfChanged(), Is.True);
            Assert.That(prof.ReleaseDepth, Is.EqualTo(0.6));
        }

        [Test]
        public void A_profile_from_text_has_nothing_to_reload()
        {
            Assert.That(Profile.FromText(DefaultText()).ReloadIfChanged(), Is.False);
        }

        [Test]
        public void Dev_override_wins_on_first_load_and_an_invalid_one_throws()
        {
            var storage = new MemoryStorage();
            var data = new DictionaryDataSource().With(Profile.DataKey, DefaultText());
            storage.PutText(Profile.DevOverrideFile, DefaultText().Replace("cycle_length = 7", "cycle_length = 5"));
            Assert.That(Profile.Load(data, storage).CycleLength, Is.EqualTo(5));
            storage.PutText(Profile.DevOverrideFile, "nonsense = [");
            Assert.Throws<ProfileException>(() => Profile.Load(data, storage));
        }

        [Test]
        public void Goal_pool_order_does_not_depend_on_the_order_in_the_file()
        {
            var swapped = DefaultText().Replace("11 = { score = 0.4, collect = 0.6 }", "11 = { collect = 0.6, score = 0.4 }");
            Assert.That(Profile.FromText(swapped).Evaluate(30).GoalPool, Is.EqualTo(Shipped().Evaluate(30).GoalPool));
        }

        [Test]
        public void Level_params_have_value_equality()
        {
            Assert.That(Shipped().Evaluate(36), Is.EqualTo(Shipped().Evaluate(36)));
            Assert.That(Shipped().Evaluate(36), Is.Not.EqualTo(Shipped().Evaluate(37)));
        }
    }
}
