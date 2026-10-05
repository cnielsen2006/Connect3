using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.CoreTests
{
    /// <summary>Tests (TRD §9.1 Core/Match).</summary>
    public class MatchTests
    {
        static (QualifierKind, int)[] Kinds(MatchGroup g) => g.Qualifiers.Select(q => (q.Kind, q.Length)).OrderBy(k => k).ToArray();

        [Test]
        public void No_match()
        {
            var b = BoardFrom(new[] { "0 1 0", "1 0 1", "0 1 0" });
            Assert.That(Match.FindGroups(b), Is.Empty);
        }

        [TestCase(3, QualifierKind.Run3)]
        [TestCase(4, QualifierKind.Run4)]
        [TestCase(5, QualifierKind.Run5)]
        public void Run3_run4_run5(int n, QualifierKind kind)
        {
            string row = string.Join(" ", Enumerable.Repeat("0", n).Concat(new[] { "1", "2" }));
            string second = string.Join(" ", Enumerable.Repeat(new[] { "3", "4" }, 5).SelectMany(x => x)).Substring(0, row.Length);
            var b = BoardFrom(new[] { row, second });
            var groups = Match.FindGroups(b);
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].Cells.Count, Is.EqualTo(n));
            Assert.That(Kinds(groups[0]), Is.EqualTo(new[] { (kind, n) }));
        }

        [Test]
        public void Vertical_run()
        {
            var b = BoardFrom(new[] { "0 1", "0 2", "0 1" });
            var g = Match.FindGroups(b).Single();
            Assert.That(g.Cells.ToArray(), Is.EqualTo(Enumerable.Range(0, 3).Select(y => Cell(b, 0, y)).ToArray()));
        }

        [TestCase("L", "0 0 0 1", "0 1 2 3", "0 3 4 2", "1 2 3 4")]
        [TestCase("T", "0 0 0 1", "1 0 2 3", "2 0 3 2", "1 2 3 4")]
        [TestCase("+", "1 0 2 3", "0 0 0 1", "2 0 3 2", "1 2 3 4")]
        public void L_T_plus_are_crosses(string name, string r0, string r1, string r2, string r3)
        {
            var g = Match.FindGroups(BoardFrom(new[] { r0, r1, r2, r3 })).Single();
            Assert.That(g.Cells.Count, Is.EqualTo(5), name);
            Assert.That(g.Qualifiers.Select(q => q.Kind).OrderBy(k => k).ToArray(),
                Is.EqualTo(new[] { QualifierKind.Run3, QualifierKind.Run3, QualifierKind.Cross }), name);
        }

        [Test]
        public void Two_separate_groups()
        {
            var b = BoardFrom(new[] { "0 0 0 1 2 2 2", "3 4 3 4 3 4 3" });
            var groups = Match.FindGroups(b);
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups.Select(Kinds).ToArray(), Is.EqualTo(new[]
            {
                new[] { (QualifierKind.Run3, 3) },
                new[] { (QualifierKind.Run3, 3) },
            }));
        }

        [Test]
        public void Same_color_parallel_runs_do_not_merge()
        {
            var b = BoardFrom(new[] { "0 0 0", "0 0 0", "1 2 1" });
            // vertical pairs make no runs; the two rows are separate groups
            Assert.That(Match.FindGroups(b).Count, Is.EqualTo(2));
        }

        [Test]
        public void Four_run_plus_crossing_run3_counts_all_qualifiers()
        {
            var b = BoardFrom(new[] { "0 0 0 0 1", "1 0 2 3 4", "2 0 3 1 2", "3 4 1 2 1" });
            var g = Match.FindGroups(b).Single();
            Assert.That(Kinds(g), Is.EqualTo(new[] { (QualifierKind.Run3, 3), (QualifierKind.Run4, 4), (QualifierKind.Cross, 0) }));
            Assert.That(g.Cells.Count, Is.EqualTo(6));
        }

        [Test]
        public void Holes_split_lines()
        {
            var b = BoardFrom(new[] { "0 0 . 0 0", "1 2 3 4 1" });
            Assert.That(Match.FindGroups(b), Is.Empty);
        }

        [Test]
        public void HasMatchThrough_and_PickColor_avoid_runs()
        {
            var b = BoardFrom(new[] { "0 0 . 2", "2 3 4 1", "2 1 4 1" }, colors: 4); // column 3 has 1,1 below
            Assert.That(Match.HasMatchThrough(BoardFrom(new[] { "0 0 0" }), new[] { new CellId(0) }), Is.True);
            var rng = Rng.Substream(1, "tiles");
            b.Put(Cell(b, 3, 0), null);
            for (int i = 0; i < 50; i++)
                Assert.That(Match.PickColor(b, Cell(b, 3, 0), rng), Is.Not.EqualTo(1)); // 1,1 below would make a run of 3
            Assert.That(new Qualifier(QualifierKind.Cross).Length, Is.EqualTo(0));
        }

        // --- extras beyond the reference tests -------------------------------------------------

        [Test]
        public void Groups_are_ordered_by_first_cell_and_cells_ascending()
        {
            var b = BoardFrom(new[] { "1 2 1 2 1", "2 1 2 1 2", "0 0 0 3 4", "3 4 1 0 0", "4 3 2 0 3", "1 2 4 0 2" }, colors: 5);
            var groups = Match.FindGroups(b);
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].Cells[0], Is.LessThan(groups[1].Cells[0]));
            foreach (var g in groups) Assert.That(g.Cells.ToArray(), Is.Ordered);
        }

        [Test]
        public void FindRunsThrough_matches_FindRuns_on_a_board_with_one_match()
        {
            var b = BoardFrom(new[] { "1 2 1 2 1", "2 1 2 1 2", "0 0 0 3 4", "3 4 1 0 3" });
            var all = Match.FindRuns(b);
            var through = Match.FindRunsThrough(b, new[] { Cell(b, 1, 2) });
            Assert.That(through, Is.EqualTo(all));
        }

        [Test]
        public void RunIfPlaced_ignores_the_tile_at_the_cell()
        {
            var b = BoardFrom(new[] { "0 0 1 0 0" });
            Assert.That(Match.RunIfPlaced(b, Cell(b, 2, 0), 0), Is.EqualTo(5));
            Assert.That(Match.RunIfPlaced(b, Cell(b, 2, 0), 1), Is.EqualTo(1));
        }

        [Test]
        public void PickColor_falls_back_to_the_shortest_run_when_every_color_matches()
        {
            // Both colors complete a run at the empty cell: color 0 makes 3 (0 0 _), color 1 makes 4 (_ 1 1 1).
            var b = BoardFrom(new[] { "0 0 0 1 1 1" }, colors: 2);
            b.Put(Cell(b, 2, 0), null);
            var rng = Rng.Substream(4, "tiles");
            var picked = new HashSet<int>();
            for (int i = 0; i < 40; i++) picked.Add(Match.PickColor(b, Cell(b, 2, 0), rng));
            Assert.That(picked, Is.EqualTo(new HashSet<int> { 0 }));
        }
    }
}
