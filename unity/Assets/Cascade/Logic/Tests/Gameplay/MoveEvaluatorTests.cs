using System.Linq;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Tests.Support;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.Gameplay
{
    /// <summary>The shallow move value shared by hints and the bot (TRD §7).</summary>
    public class MoveEvaluatorTests
    {
        static readonly RuleSet Rules = RuleSets.From(TestDials.Default());
        static readonly MoveWeights W = new MoveWeights(); // 10, 3, 0.01

        static Board CrossBoard() =>
            BoardFrom(new[] { "1 2 0 3 4", "2 3 0 4 1", "0 0 1 2 3", "3 4 0 1 2", "4 1 2 3 0" });

        [Test]
        public void Value_of_a_plain_match_is_points_times_weight()
        {
            var b = CrossBoard();
            double v = MoveEvaluator.Value(b, Cell(b, 2, 2), Cell(b, 2, 3), Rules, W);
            // the swap makes the L of five 0s: two Run3 and a Cross = 60 + 60 + 150 points, and the Cross earns a Bomb
            Assert.That(v, Is.EqualTo(270 * 0.01 + 3.0).Within(1e-12));
        }

        [Test]
        public void Value_leaves_the_board_as_it_was()
        {
            var b = CrossBoard();
            var before = Snapshot(b);
            MoveEvaluator.Value(b, Cell(b, 2, 2), Cell(b, 2, 3), Rules, W);
            Assert.That(Snapshot(b), Is.EqualTo(before));
        }

        [Test]
        public void Wanted_colors_add_goal_progress()
        {
            var b = CrossBoard();
            double plain = MoveEvaluator.Value(b, Cell(b, 2, 2), Cell(b, 2, 3), Rules, W);
            double wanted = MoveEvaluator.Value(b, Cell(b, 2, 2), Cell(b, 2, 3), Rules, W, new[] { 0 });
            Assert.That(wanted - plain, Is.EqualTo(5 * 10.0).Within(1e-9)); // five 0 tiles clear
            double other = MoveEvaluator.Value(b, Cell(b, 2, 2), Cell(b, 2, 3), Rules, W, new[] { 4 });
            Assert.That(other, Is.EqualTo(plain));
        }

        [Test]
        public void Earned_specials_count_only_when_enabled()
        {
            var five = BoardFrom(new[]
            {
                "1 2 3 4 1", "2 3 4 1 2", "0 0 1 0 0", "3 4 0 1 3", "4 1 2 3 4",
            });
            double all = MoveEvaluator.Value(five, Cell(five, 2, 2), Cell(five, 2, 3), Rules, W);
            five.SpecialsEnabled = new[] { "Line" };
            double lineOnly = MoveEvaluator.Value(five, Cell(five, 2, 2), Cell(five, 2, 3), Rules, W);
            Assert.That(all - lineOnly, Is.EqualTo(3.0).Within(1e-12)); // a Run5 earns a Color piece
        }

        [Test]
        public void A_special_blast_adds_points_for_the_extra_tiles()
        {
            // the Line piece (row axis) sits in the new run of four and also clears the rest of its row
            string[] plainRows = { "1 2 3 4 1", "2 3 4 1 2", "0 0 1 0 2", "3 4 0 1 3", "4 1 2 3 4" };
            string[] lineRows = { "1 2 3 4 1", "2 3 4 1 2", "0 0 1 0h 2", "3 4 0 1 3", "4 1 2 3 4" };
            var plain = BoardFrom(plainRows);
            var line = BoardFrom(lineRows);
            double p = MoveEvaluator.Value(plain, Cell(plain, 2, 2), Cell(plain, 2, 3), Rules, W);
            double l = MoveEvaluator.Value(line, Cell(line, 2, 2), Cell(line, 2, 3), Rules, W);
            Assert.That(l - p, Is.EqualTo(20 * 0.01).Within(1e-12)); // one extra tile cleared by the blast
        }

        [Test]
        public void Color_color_swap_values_the_whole_board()
        {
            var b = BoardFrom(new[] { "0c 1c 2", "1 2 0", "2 0 1" }, colors: 3);
            double v = MoveEvaluator.Value(b, Cell(b, 0, 0), Cell(b, 1, 0), Rules, W);
            Assert.That(v, Is.EqualTo(0.0).Within(1e-9)); // nothing matched, nothing outside the cleared set
            var wanted = MoveEvaluator.Value(b, Cell(b, 0, 0), Cell(b, 1, 0), Rules, W, new[] { 0, 1, 2 });
            Assert.That(wanted, Is.EqualTo(9 * 10.0).Within(1e-9)); // every tile is cleared
        }

        [Test]
        public void Evaluator_agrees_with_resolve_on_which_moves_score()
        {
            var level = new Level(1, 77, 7, 7, new (int, int)[0], 4, new[] { Goal.Score(Huge) }, 20, 1, 2,
                new string[0], null, 0.5, 0.5, null, 1);
            var s = new Session(level, Rules);
            var scratch = s.Board.Copy();
            foreach (var (a, b) in s.LegalMoves)
            {
                double v = MoveEvaluator.Value(scratch, a, b, Rules, W);
                Assert.That(v, Is.GreaterThan(0), $"{a}-{b}");
                Assert.That(Core.Rules.Resolve(s.Board, a, b, new Streams(1), Rules).Events[0], Is.TypeOf<Swapped>());
            }
        }

        const int Huge = 1_000_000_000;
    }
}
