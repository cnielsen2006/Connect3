using System.Linq;
using Cascade.Core;
using NUnit.Framework;

namespace Cascade.Tests.CoreTests
{
    /// <summary>The Grid preset of <see cref="Topology"/> (TRD §3) and the <see cref="ValueList{T}"/> value type.</summary>
    public class TopologyTests
    {
        [Test]
        public void Grid_ids_positions_and_neighbors()
        {
            var t = Topology.Grid(3, 2);
            Assert.That(t.Cells.Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(t.Pos(4), Is.EqualTo(new Pos(0, 1, 1)));
            Assert.That(t.CellAt(2, 1), Is.EqualTo((CellId?)new CellId(5)));
            Assert.That(t.CellAt(3, 0), Is.Null);
            // neighbor order: up, left, right, down
            Assert.That(t.Neighbors(4).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(t.Neighbors(0).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(t.SwapPairs.Count, Is.EqualTo(7)); // 4 horizontal + 3 vertical edges
        }

        [Test]
        public void Grid_lines_are_split_at_holes()
        {
            var t = Topology.Grid(5, 2, new[] { (2, 0) });
            Assert.That(t.Contains(2), Is.False);
            var rows = t.Lines.Where(l => l.Axis == 0).Select(l => l.Cells.Select(c => c.Value).ToArray()).ToArray();
            Assert.That(rows[0], Is.EqualTo(new[] { 0, 1 }));
            Assert.That(rows[1], Is.EqualTo(new[] { 3, 4 }));
            Assert.That(rows[2], Is.EqualTo(new[] { 5, 6, 7, 8, 9 }));
            Assert.That(t.Neighbors(1).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 0, 6 })); // no neighbor through the hole
            Assert.That(t.LineThrough(1, 0).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(t.LineThrough(1, 1).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 1, 6 }));
        }

        [Test]
        public void Area_is_a_clipped_square_on_the_grid_and_neighbor_hops_by_default()
        {
            var t = Topology.Grid(4, 4);
            Assert.That(t.Area(0, 1).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 0, 1, 4, 5 }));
            Assert.That(t.Area(5, 1).Count, Is.EqualTo(9));
            Assert.That(t.Area(5, 0).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 5 }));
            // a topology without an area function: hops over neighbors (a plus shape for radius 1)
            var cells = t.Cells.Select(c => (c, t.Pos(c)));
            var nb = t.Cells.Select(c => (c, t.Neighbors(c).ToArray()));
            var generic = new Topology(cells, nb, t.Lines);
            Assert.That(generic.Area(5, 1).Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 1, 4, 5, 6, 9 }));
            Assert.That(generic.Area(5, 2).Count, Is.EqualTo(11)); // the diamond clipped by the board edge
        }

        [Test]
        public void LinesThrough_lists_row_then_column()
        {
            var t = Topology.Grid(3, 3);
            var slots = t.LinesThrough(4);
            Assert.That(slots.Select(s => s.Line.Axis).ToArray(), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(slots.Select(s => s.Index).ToArray(), Is.EqualTo(new[] { 1, 1 }));
        }

        [Test]
        public void CellId_and_ValueList_basics()
        {
            CellId a = 3;
            Assert.That(a == 3, Is.True);
            Assert.That(a < 4, Is.True);
            Assert.That((int)a, Is.EqualTo(3));
            Assert.That(new CellId(3).Equals(a));
            var l1 = ValueList.Of(1, 2, 3);
            var l2 = new[] { 1, 2, 3 };
            Assert.That(l1.Equals((ValueList<int>)l2));
            Assert.That(l1 == (ValueList<int>)new[] { 1, 2, 4 }, Is.False);
            Assert.That(l1.ToString(), Is.EqualTo("[1, 2, 3]"));
            l2[0] = 99; // the list copied its input
            Assert.That(l1[0], Is.EqualTo(1));
            Assert.That(ValueList<int>.Empty.Count, Is.EqualTo(0));
        }
    }
}
