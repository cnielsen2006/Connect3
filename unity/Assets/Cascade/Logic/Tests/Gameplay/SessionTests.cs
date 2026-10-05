using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Tests.Support;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.Gameplay
{
    /// <summary>Session tests (TRD §9.1 Gameplay).</summary>
    public class SessionTests
    {
        static readonly RuleSet Rules = RuleSets.From(TestDials.Default());

        static Board CrossBoard() =>
            BoardFrom(new[] { "1 2 0 3 4", "2 3 0 4 1", "0 0 1 2 3", "3 4 0 1 2", "4 1 2 3 0" });

        static Board RunBoard(bool five) =>
            BoardFrom(new[]
            {
                "1 2 3 4 1",
                "2 3 4 1 2",
                "0 0 1 " + (five ? "0 0" : "0 2"),
                "3 4 0 1 3",
                "4 1 2 3 4",
            });

        const int Huge = 1_000_000_000;

        static Level MakeLevel(IEnumerable<Goal> goals, int moves = 5, int star2 = Huge, int star3 = Huge + 1,
            string[]? specials = null) =>
            new Level(1, 1, 5, 5, new (int, int)[0], 5, goals.ToArray(), moves, star2, star3,
                specials ?? new[] { "Line", "Bomb", "Color" }, null, 0.5, 0.5, null, 1);

        static Session Start(IEnumerable<Goal> goals, Board? board = null, int moves = 5, int star2 = Huge, int star3 = Huge + 1) =>
            new Session(MakeLevel(goals, moves, star2, star3), Rules, null, new Streams(1), board ?? CrossBoard());

        static readonly ((int, int), (int, int)) Good = ((2, 2), (2, 3)); // makes the L/cross in CrossBoard
        static readonly ((int, int), (int, int)) Bad = ((0, 0), (1, 0)); // rejected in CrossBoard

        static MoveResult Swap(Session s, ((int, int), (int, int)) move)
        {
            var ((ax, ay), (bx, by)) = move;
            return s.TrySwap(Cell(s.Board, ax, ay), Cell(s.Board, bx, by));
        }

        [Test]
        public void Rejected_swap_does_not_use_a_move()
        {
            var s = Start(new[] { Goal.Score(1_000_000) });
            var r = Swap(s, Bad);
            Assert.That(r.Outcome, Is.EqualTo(MoveOutcome.Rejected));
            Assert.That(r.Events.Count, Is.EqualTo(1));
            Assert.That(r.Events[0], Is.TypeOf<SwapRejected>());
            Assert.That(s.MovesLeft, Is.EqualTo(5));
            Assert.That(s.Score, Is.EqualTo(0));
            Assert.That(s.Status, Is.EqualTo(SessionStatus.Playing));
            Assert.That(r.Before, Is.SameAs(r.After));
            // off-board / non-neighbor swaps are rejected too, never thrown
            Assert.That(s.TrySwap(0, 24).Events[0], Is.TypeOf<SwapRejected>());
            Assert.That(s.TrySwap(0, 999).Events[0], Is.TypeOf<SwapRejected>());
            Assert.That(s.TrySwap(-1, 0).Events[0], Is.TypeOf<SwapRejected>());
            Assert.That(s.MovesLeft, Is.EqualTo(5));
        }

        [Test]
        public void Valid_swap_uses_a_move_and_scores()
        {
            var s = Start(new[] { Goal.Score(1_000_000) });
            var r = Swap(s, Good);
            Assert.That(r.Outcome, Is.EqualTo(MoveOutcome.Played));
            Assert.That(s.MovesLeft, Is.EqualTo(4));
            Assert.That(s.MovesUsed, Is.EqualTo(1));
            Assert.That(s.Score, Is.EqualTo(r.Events.OfType<Scored>().Sum(e => e.Points)));
            Assert.That(s.Score, Is.GreaterThanOrEqualTo(270));
            Assert.That(s.Progress.ToArray(), Is.EqualTo(new[] { s.Score }));
            Assert.That(s.Stars, Is.EqualTo(0));
            Assert.That(s.Status, Is.EqualTo(SessionStatus.Playing));
        }

        [Test]
        public void Move_result_carries_boards_events_and_state()
        {
            var s = Start(new[] { Goal.Score(1_000_000), Goal.Collect(1_000_000, 0) });
            var boardBefore = s.Board;
            var colorsBefore = ColorsText(boardBefore);
            var r = Swap(s, Good);
            Assert.That(r.Before, Is.SameAs(boardBefore));
            Assert.That(ColorsText(r.Before), Is.EqualTo(colorsBefore), "the before board is not touched");
            Assert.That(r.After, Is.SameAs(s.Board));
            Assert.That(r.After, Is.Not.SameAs(r.Before));
            Assert.That(r.Events[0], Is.TypeOf<Swapped>());
            Assert.That(r.ScoreDelta, Is.EqualTo(s.Score));
            Assert.That(r.Score, Is.EqualTo(s.Score));
            Assert.That(r.MovesLeft, Is.EqualTo(4));
            Assert.That(r.Status, Is.EqualTo(SessionStatus.Playing));
            Assert.That(r.Progress.ToArray(), Is.EqualTo(s.Progress.ToArray()));
            int second = s.Score;
            var r2 = Swap(s, ((0, 0), (1, 0)));
            if (r2.Played) Assert.That(r2.ScoreDelta, Is.EqualTo(s.Score - second));
            else Assert.That(r2.ScoreDelta, Is.EqualTo(0));
        }

        [Test]
        public void Win_is_immediate_and_ends_play()
        {
            var s = Start(new[] { Goal.Score(60) }, moves: 3);
            var r = Swap(s, Good);
            Assert.That(s.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(r.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(s.Stars, Is.EqualTo(1));
            Assert.That(s.MovesLeft, Is.EqualTo(2));
            var after = Swap(s, Good); // finished: nothing more happens
            Assert.That(after.Outcome, Is.EqualTo(MoveOutcome.Ignored));
            Assert.That(after.Events, Is.Empty);
            Assert.That(s.MovesLeft, Is.EqualTo(2));
            Assert.That(s.Hint(), Is.Null);
        }

        [Test]
        public void Win_on_the_last_move_beats_losing()
        {
            var s = Start(new[] { Goal.Score(60) }, moves: 1);
            Swap(s, Good);
            Assert.That(s.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(s.MovesLeft, Is.EqualTo(0));
        }

        [Test]
        public void Lose_when_moves_run_out()
        {
            var s = Start(new[] { Goal.Score(1_000_000) }, moves: 1);
            var r = Swap(s, Good);
            Assert.That(s.Status, Is.EqualTo(SessionStatus.Lost));
            Assert.That(r.Status, Is.EqualTo(SessionStatus.Lost));
            Assert.That(s.Stars, Is.EqualTo(0));
            Assert.That(s.MovesLeft, Is.EqualTo(0));
            Assert.That(Swap(s, Bad).Events, Is.Empty);
        }

        [Test]
        public void Stars_follow_score_thresholds()
        {
            var reference = Start(new[] { Goal.Score(60) });
            Swap(reference, Good);
            int score = reference.Score;
            foreach (var (star2, star3, want) in new[] { (score + 1, score + 2, 1), (score, score + 1, 2), (score, score, 3) })
            {
                var s = Start(new[] { Goal.Score(60) }, star2: star2, star3: star3);
                Swap(s, Good);
                Assert.That(s.Stars, Is.EqualTo(want), $"{star2}, {star3}");
            }
        }

        [Test]
        public void Collect_progress_counts_cleared_tiles_of_the_goal_colors()
        {
            var b = CrossBoard();
            var colorOf = new Dictionary<int, int>();
            foreach (var c in b.Topology.Cells) colorOf[b.TileAt(c)!.Id] = b.TileAt(c)!.Color;
            var s = Start(new[] { Goal.Collect(1_000_000, 0), Goal.Collect(1_000_000, 0, 1) }, board: b);
            var r = Swap(s, Good);
            var cleared = new List<int>();
            foreach (var e in r.Events) // independent recount from the raw event stream
            {
                if (e is Spawned sp) colorOf[sp.TileId] = sp.Color;
                else if (e is Cleared cl) foreach (int t in cl.TileIds) cleared.Add(colorOf[t]);
            }
            Assert.That(s.Progress.ToArray(), Is.EqualTo(new[] { cleared.Count(c => c == 0), cleared.Count(c => c == 0 || c == 1) }));
            Assert.That(s.Progress[0], Is.GreaterThanOrEqualTo(5)); // the L of five 0s was cleared
            Assert.That(s.GoalDescriptions(withProgress: true)[0], Does.EndWith($"({s.Progress[0]}/1000000)"));
            Assert.That(s.GoalDescriptions()[0], Does.Contain("Red"));
        }

        [Test]
        public void Combo_needs_every_goal()
        {
            var s = Start(new[] { Goal.Score(60), Goal.Collect(1_000_000, 0) }, moves: 3);
            Swap(s, Good);
            Assert.That(s.Status, Is.EqualTo(SessionStatus.Playing));
            Assert.That(s.Progress[0], Is.EqualTo(60));
            var s2 = Start(new[] { Goal.Score(60), Goal.Collect(3, 0) }, moves: 3);
            Swap(s2, Good);
            Assert.That(s2.Status, Is.EqualTo(SessionStatus.Won));
        }

        [Test]
        public void Specials_enabled_filters_earned_specials()
        {
            (List<SpecialCreated> Made, List<Event> Events) Created(string[] enabled)
            {
                var b = RunBoard(true);
                b.SpecialsEnabled = enabled;
                var level = MakeLevel(new[] { Goal.Score(Huge) }, specials: enabled);
                var s = new Session(level, Rules, null, new Streams(1), b);
                var r = Swap(s, Good);
                return (r.Events.OfType<SpecialCreated>().ToList(), r.Events.ToList());
            }

            Assert.That(Created(new[] { "Line", "Bomb", "Color" }).Made.Count, Is.EqualTo(1)); // Run5 -> Color
            var (made, events) = Created(new[] { "Line" });
            Assert.That(made, Is.Empty);
            Assert.That(events.OfType<Scored>().Any(e => e.Points >= 200)); // still scores
            Assert.That(Created(new string[0]).Made, Is.Empty);
        }

        [Test]
        public void Copy_is_independent_and_hint_is_legal()
        {
            var s = Start(new[] { Goal.Score(1_000_000) });
            var c = s.Copy();
            Swap(c, Good);
            Assert.That(s.MovesLeft, Is.EqualTo(5));
            Assert.That(s.Score, Is.EqualTo(0));
            Assert.That(c.MovesLeft, Is.EqualTo(4));
            var (a, b) = s.Hint()!.Value;
            Assert.That(s.LegalMoves, Does.Contain((a, b)));
            Assert.That(s.Copy().TrySwap(a, b).Events[0], Is.TypeOf<Swapped>());
        }

        [Test]
        public void Session_matches_level_board_and_is_deterministic()
        {
            var level = MakeLevel(new[] { Goal.Score(1_000_000) });
            var a = new Session(level, Rules);
            var b = new Session(level, Rules);
            Assert.That(a.Board.Topology.Cells.Select(c => a.Board.TileAt(c)),
                Is.EqualTo(b.Board.Topology.Cells.Select(c => b.Board.TileAt(c))));
            Assert.That(Match.FindRuns(a.Board), Is.Empty);
            var ma = a.Hint();
            Assert.That(ma, Is.EqualTo(b.Hint()));
            Assert.That(a.TrySwap(ma!.Value.A, ma.Value.B).Events, Is.EqualTo(b.TrySwap(ma.Value.A, ma.Value.B).Events));
        }

        [Test]
        public void Hint_does_not_disturb_the_board_or_the_legal_move_list()
        {
            var level = MakeLevel(new[] { Goal.Collect(50, 1) });
            var s = new Session(level, Rules);
            var before = ColorsText(s.Board);
            var legal = s.LegalMoves.ToList();
            var best = s.BestMoves();
            Assert.That(ColorsText(s.Board), Is.EqualTo(before));
            Assert.That(s.LegalMoves.ToList(), Is.EqualTo(legal));
            Assert.That(best, Is.Not.Empty);
            foreach (var m in best) Assert.That(legal, Does.Contain(m));
            Assert.That(s.Hint(), Is.EqualTo(best[0]));
        }

        [Test]
        public void Rules_snapshot_can_be_replaced_between_moves()
        {
            var s = Start(new[] { Goal.Score(Huge) });
            s.Rules = new RuleSet { Scoring = new ScoringRules { Run3 = 600 } };
            var r = Swap(s, Good);
            var reference = Start(new[] { Goal.Score(Huge) });
            var r0 = Swap(reference, Good);
            Assert.That(r.ScoreDelta, Is.GreaterThan(r0.ScoreDelta));
        }

        [Test]
        public void Goal_descriptions_without_progress()
        {
            var s = Start(new[] { Goal.Score(1000), Goal.Collect(12, 1, 3) });
            var lines = s.GoalDescriptions();
            Assert.That(lines[0], Does.Contain("1,000"));
            Assert.That(lines[1], Does.Contain("Blue").And.Contain("Yellow"));
            Assert.That(s.RemainingColors.ToArray(), Is.EqualTo(new[] { 1, 3 }));
        }
    }
}
