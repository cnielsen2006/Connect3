using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.CoreTests
{
    /// <summary>Core tests: goals, the tracker and Level.</summary>
    public class GoalsLevelTests
    {
        [Test]
        public void Goal_validation_and_json()
        {
            var g = Goal.Collect(12, 1, 3);
            var w = new JsonWriter();
            g.WriteJson(w);
            Assert.That(w.ToString(), Is.EqualTo("{\"kind\":\"collect\",\"target\":12,\"colors\":[1,3]}"));
            Assert.That(Goal.FromJson(JsonValue.Parse(w.ToString())), Is.EqualTo(g));
            Assert.Throws<ArgumentException>(() => new Goal((GoalKind)7, 1));
            Assert.Throws<ArgumentException>(() => Goal.Score(0));
            Assert.Throws<ArgumentException>(() => new Goal(GoalKind.Collect, 5));
            Assert.Throws<FormatException>(() => Goal.FromJson(JsonValue.Parse("{\"kind\":\"nope\",\"target\":1,\"colors\":[]}")));
        }

        [Test]
        public void Progress_is_clamped_and_describes_itself()
        {
            var goals = new[] { Goal.Score(100), Goal.Collect(4, 0, 2) };
            Assert.That(GoalProgress.Progress(goals, 250, new[] { 1, 9, 2 }), Is.EqualTo(new[] { 100, 3 }));
            Assert.That(GoalProgress.AllMet(goals, 250, new[] { 1, 0, 2 }), Is.False);
            Assert.That(GoalProgress.AllMet(goals, 100, new[] { 2, 0, 2 }), Is.True);
            Assert.That(goals[0].Describe(), Does.Contain("100"));
            Assert.That(goals[1].Describe(), Does.Contain("Red"));
            Assert.That(Goal.Score(1234567).Describe(), Is.EqualTo("Score 1,234,567 points"));
            Assert.That(Goal.Collect(12, 0, 1).Describe(), Is.EqualTo("Clear 12 Red / Blue pieces"));
            Assert.That(Goal.Collect(3, 9).Describe(new[] { "A", "B" }), Is.EqualTo("Clear 3 B pieces")); // 9 % 2 = 1
        }

        [Test]
        public void Tracker_follows_spawns_shuffles_and_clears()
        {
            var b = BoardFrom(new[] { "0 1 2", "1 2 0", "2 0 1" }, colors: 3);
            var ids = b.Topology.Cells.Select(c => b.TileAt(c)!.Id).ToArray(); // colors 0 1 2 / 1 2 0 / 2 0 1
            var t = new GoalTracker(new[] { Goal.Collect(3, 0), Goal.Score(50) }, b);
            t.Apply(new Event[] { new Cleared(new[] { ids[0], ids[1] }, 0), new Scored(30, 0) }); // colors 0 and 1
            Assert.That(t.Progress(), Is.EqualTo(new[] { 1, 30 }));
            Assert.That(t.Met(), Is.False);
            t.Apply(new Event[] { new Spawned(100, 0, 0, 0), new Shuffled(new[] { (ids[2], 0) }) }); // new 0 tile; a 2 becomes 0
            t.Apply(new Event[] { new Cleared(new[] { 100, ids[2] }, 0), new Scored(30, 1) });
            Assert.That(t.Progress(), Is.EqualTo(new[] { 3, 50 }));
            Assert.That(t.Met(), Is.True);
            Assert.That(t.RemainingColors(), Is.Empty);
            Assert.That(t.Copy().Progress(), Is.EqualTo(t.Progress()));
        }

        [Test]
        public void Tracker_copy_is_independent_and_remaining_colors_are_sorted()
        {
            var b = BoardFrom(new[] { "0 1 2", "1 2 0", "2 0 1" }, colors: 3);
            var t = new GoalTracker(new[] { Goal.Collect(2, 2, 1), Goal.Collect(1, 0) }, b);
            Assert.That(t.RemainingColors(), Is.EqualTo(new[] { 0, 1, 2 }));
            var c = t.Copy();
            c.Apply(new Event[] { new Scored(10, 0), new Cleared(new[] { 0 }, 0) });
            Assert.That(t.Score, Is.EqualTo(0));
            Assert.That(c.Score, Is.EqualTo(10));
            Assert.That(c.Collected[0], Is.EqualTo(1));
            Assert.That(t.Collected[0], Is.EqualTo(0));
            Assert.That(c.RemainingColors(), Is.EqualTo(new[] { 1, 2 }));
        }

        static Level MakeLevel() => new Level(7, (1UL << 63) + 5, 6, 6, new[] { (0, 0), (5, 0) }, 5,
            new[] { Goal.Score(500), Goal.Collect(9, 2) }, 20, 600, 900, new[] { "Line", "Bomb" }, "Line pieces", 0.4, 0.8, 0.75, 1);

        [Test]
        public void Level_json_round_trip_and_board_options()
        {
            var lvl = MakeLevel();
            var again = Level.FromJson(lvl.ToJson());
            Assert.That(again, Is.EqualTo(lvl));
            var board = lvl.BuildBoard(new Streams(lvl.Seed));
            Assert.That(board.SpecialsEnabled, Is.EqualTo(new[] { "Line", "Bomb" }));
            Assert.That(board.Topology.Cells.Count, Is.EqualTo(34));
            Assert.That(board.Copy().SpecialsEnabled, Is.EqualTo(board.SpecialsEnabled));
            Assert.That(board.Topology.Cells.Any(c => board.Topology.Pos(c).X == 0 && board.Topology.Pos(c).Y == 0), Is.False);
            Assert.That(Match.FindRuns(board), Is.Empty);
            Assert.That(Moves.FindLegal(board), Is.Not.Empty);
        }

        [Test]
        public void Level_json_writes_the_seed_as_text_and_keeps_reference_member_order()
        {
            string json = MakeLevel().ToJson();
            Assert.That(json, Does.Contain("\"seed\":\"9223372036854775813\""));
            var names = Newtonsoft.Json.Linq.JObject.Parse(json).Properties().Select(p => p.Name).ToArray();
            Assert.That(names, Is.EqualTo(new[]
            {
                "number", "seed", "width", "height", "holes", "colors", "goals", "move_limit", "star2", "star3",
                "specials", "intro", "difficulty", "target_win_rate", "measured_win_rate", "generator_version",
            }));
            // null intro and null measured rate round trip
            var l = MakeLevel() with { Intro = null, MeasuredWinRate = null };
            Assert.That(Level.FromJson(l.ToJson()), Is.EqualTo(l));
            Assert.That(l.ToJson(), Does.Contain("\"intro\":null").And.Contain("\"measured_win_rate\":null"));
        }

        [Test]
        public void Level_build_board_is_deterministic()
        {
            var lvl = MakeLevel();
            var a = lvl.BuildBoard(new Streams(lvl.Seed));
            var b = lvl.BuildBoard(new Streams(lvl.Seed));
            Assert.That(Snapshot(a), Is.EqualTo(Snapshot(b)));
        }
    }
}
