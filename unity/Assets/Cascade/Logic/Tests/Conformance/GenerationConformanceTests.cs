using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Conformance
{
    /// <summary>
    /// Generation conformance (TRD §9.2): <c>profiles/evaluations.json</c>, <c>levels/*.json</c> and
    /// <c>bot/runs.json</c> must match the reference exactly. Fixtures are an oracle and are
    /// never regenerated from C#; a difference is a port bug or an undocumented reference behavior.
    /// </summary>
    public class GenerationConformanceTests
    {
        static GenerationConformance Make() => new GenerationConformance(
            parts => ConformanceFiles.ReadText(parts),
            folder => Directory.GetFiles(ConformanceFiles.FilePath(folder)).Select(Path.GetFileName).Select(f => f!)
                .OrderBy(f => f, System.StringComparer.Ordinal).ToArray(),
            TestDials.Profile(), TestDials.NoBudget());

        public static IEnumerable<string> LevelFileNames() => Make().LevelFiles();

        static void AssertPassed(ConformanceReport r)
        {
            foreach (var line in r.Inexact) TestContext.Out.WriteLine("not == but within 1e-12: " + line);
            Assert.That(r.Failures, Is.Empty, string.Join("\n", r.Failures.Take(20)));
            Assert.That(r.Checks, Is.GreaterThan(0));
        }

        [Test]
        public void Profile_evaluations_match_for_levels_1_to_150()
        {
            var r = Make().VerifyProfiles();
            AssertPassed(r);
            Assert.That(r.Checks, Is.GreaterThan(150 * 15));
        }

        [TestCaseSource(nameof(LevelFileNames))]
        public void Level_fixture_is_reproduced_exactly(string file) => AssertPassed(Make().VerifyLevelFile(file));

        [Test]
        public void There_are_74_level_fixtures()
        {
            Assert.That(Make().LevelFiles().Length, Is.EqualTo(74));
        }

        [Test]
        public void Bot_runs_match()
        {
            var r = Make().VerifyBot();
            AssertPassed(r);
            Assert.That(r.Checks, Is.GreaterThanOrEqualTo(20 * 3));
        }

        static readonly string[] ExtraShippedDials =
        {
            "input.touch_inner_fraction", "input.double_tap_ms", "input.tap_slop_mm", "input.pinch_min_mm",
            "input.min_touch_target_mm", "render.multisamples", "render.target_fps",
            "audio.master_volume", "audio.cascade_pitch_semitones", "audio.cue_cooldown_ms",
            "celebration.tier_1", "celebration.tier_2", "celebration.tier_3", "celebration.tier_4",
            "celebration.badge_scale_base", "celebration.badge_scale_step", "celebration.badge_ms",
            "celebration.group_stagger_ms", "celebration.particles_per_tier", "celebration.camera_punch_per_tier",
            "celebration.stinger_from_tier", "celebration.reduce_effects",
            "planner.depth", "planner.width", "planner.max_nodes", "planner.win_bonus", "planner.moves_left_weight",
            "planner.progress_weight", "planner.score_weight", "planner.max_probes", "planner.probe_ms", "planner.think_ms",
            "planner.show_ms",
        };

        static Dictionary<string, string> DialValues(string text)
        {
            var map = new Dictionary<string, string>();
            string section = "";
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') { section = line.Trim('[', ']'); continue; }
                var eq = line.IndexOf('=');
                map[section + "." + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        [Test]
        public void Shipped_dials_keep_every_conformance_dial_unchanged_and_add_only_the_listed_ones()
        {
            var shipped = DialValues(RepoFiles.ReadData("dials.toml"));
            var fixture = DialValues(ConformanceFiles.ReadText("data", "dials.toml"));
            foreach (var kv in fixture)
            {
                Assert.That(shipped.ContainsKey(kv.Key), kv.Key + " missing from the shipped dials.toml");
                Assert.That(shipped[kv.Key], Is.EqualTo(kv.Value), kv.Key + " drifted from conformance/data");
            }
            var extra = shipped.Keys.Where(k => !fixture.ContainsKey(k)).ToList();
            Assert.That(extra, Is.EquivalentTo(ExtraShippedDials));
        }

        [Test]
        public void Shipped_difficulty_profile_equals_the_conformance_copy()
        {
            string shipped = RepoFiles.ReadData("difficulty.toml").Replace("\r\n", "\n");
            string fixture = ConformanceFiles.ReadText("data", "difficulty.toml").Replace("\r\n", "\n");
            Assert.That(shipped, Is.EqualTo(fixture), "difficulty.toml drifted from conformance/data");
        }
    }
}
