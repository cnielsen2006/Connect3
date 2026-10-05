using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.CoreTests
{
    /// <summary>
    /// Tests: settle on gravity (with holes) and on test-only
    /// topologies (sideways, funnel, two layers), per TRD §9.1 Core/Flow.
    /// </summary>
    public class FlowTests
    {
        [Test]
        public void Gravity_drops_tiles_and_fills_from_source()
        {
            var b = BoardFrom(new[] { "0 1", "2 3", "4 0" });
            b.Put(Cell(b, 0, 2), null);
            b.Put(Cell(b, 0, 1), null);
            var (output, events) = Flow.Settle(b, new Streams(1));
            Assert.That(output.Topology.Cells.All(c => output.TileAt(c) != null));
            Assert.That(output.TileAt(Cell(output, 0, 2))!.Color, Is.EqualTo(0)); // the old top tile fell to the bottom
            var moved = Of<Moved>(events);
            Assert.That(moved[0].TileId, Is.EqualTo(b.TileAt(Cell(b, 0, 0))!.Id));
            Assert.That(moved[0].Path.ToArray(), Is.EqualTo(new[] { Cell(b, 0, 0), Cell(b, 0, 1), Cell(b, 0, 2) }));
            var spawned = Of<Spawned>(events);
            Assert.That(spawned.Count, Is.EqualTo(2));
            Assert.That(spawned.All(e => e.Cell == Cell(b, 0, 0)));
        }

        [Test]
        public void Gravity_skips_holes()
        {
            var b = BoardFrom(new[] { "0 1", ". 2", "3 4" });
            b.Put(Cell(b, 0, 2), null);
            var (output, events) = Flow.Settle(b, new Streams(1));
            Assert.That(output.TileAt(Cell(output, 0, 2))!.Color, Is.EqualTo(0)); // fell past the hole
            var m = Of<Moved>(events).Single();
            Assert.That(m.Path.ToArray(), Is.EqualTo(new[] { Cell(b, 0, 0), Cell(b, 0, 2) }));
            Assert.That(Of<Spawned>(events).Count, Is.EqualTo(1));
        }

        [Test]
        public void Settle_is_deterministic_and_does_not_touch_input()
        {
            var b = BoardFrom(new[] { "0 1 2", "2 3 4", "4 0 1" });
            foreach (int c in new[] { 0, 4, 8 }) b.Put(c, null);
            string before = ColorsText(b);
            var (o1, e1) = Flow.Settle(b, new Streams(5));
            var (o2, e2) = Flow.Settle(b, new Streams(5));
            Assert.That(ColorsText(b), Is.EqualTo(before));
            Assert.That(ColorsText(o1), Is.EqualTo(ColorsText(o2)));
            Assert.That(e1, Is.EqualTo(e2));
        }

        [Test]
        public void New_pieces_never_complete_a_run()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                var b = BoardFrom(new[] { "0 1 0 1 0", "1 0 1 0 1", "0 1 0 1 0", "1 0 1 0 1" }, colors: 3);
                for (int c = 0; c < 5; c++) b.Put(c, null); // empty the top row: only spawns, nothing falls
                var (output, _) = Flow.Settle(b, new Streams(seed));
                Assert.That(Match.FindRuns(output), Is.Empty);
            }
        }

        // --- test-only topologies ---------------------------------------------------------------

        static Board Custom(
            (int Cell, Pos Pos)[] cells,
            (int Cell, int[] Neighbors)[] neighbors,
            CellLine[] lines,
            (int Cell, CellOrSource[] Feed)[] feeds,
            Source[] sources,
            int colors = 3)
        {
            var topo = new Topology(
                cells.Select(c => (new CellId(c.Cell), c.Pos)),
                neighbors.Select(n => (new CellId(n.Cell), n.Neighbors.Select(i => new CellId(i)).ToArray())),
                lines);
            var flow = new Flow(feeds.Select(f => (new CellId(f.Cell), f.Feed)), sources);
            return Board.Empty(topo, flow, colors);
        }

        [Test]
        public void Sideways_flow()
        {
            // one row of 4; pieces flow left, the source enters on the right
            var cells = Enumerable.Range(0, 4).Select(i => (i, new Pos(0, i, 0))).ToArray();
            var nbr = Enumerable.Range(0, 4)
                .Select(i => (i, new[] { i - 1, i + 1 }.Where(j => j >= 0 && j < 4).ToArray())).ToArray();
            var feeds = new (int, CellOrSource[])[]
            {
                (0, new CellOrSource[] { 1 }),
                (1, new CellOrSource[] { 2 }),
                (2, new CellOrSource[] { 3 }),
                (3, new[] { CellOrSource.FromSource(new SourceId(0)) }),
            };
            var b = Custom(cells, nbr, new[] { new CellLine(0, Ids(0, 1, 2, 3)) }, feeds,
                new[] { new Source(new SourceId(0), Ids(3)) });
            b.Put(1, new Tile(b.NewTileId(), 1));
            b.Put(2, new Tile(b.NewTileId(), 2));
            var (output, events) = Flow.Settle(b, new Streams(1));
            Assert.That(output.TileAt(0)!.Color, Is.EqualTo(1));
            Assert.That(output.TileAt(1)!.Color, Is.EqualTo(2));
            Assert.That(output.TileAt(2), Is.Not.Null);
            Assert.That(output.TileAt(3), Is.Not.Null);
            Assert.That(events.OfType<Moved>().Any(e => e.Path.ToArray().SequenceEqual(Ids(1, 0))));
        }

        [Test]
        public void Funnel_link_feeds_from_a_non_adjacent_cell()
        {
            // cells 0,1 on top; 2 below them. Cell 2 pulls from 0, then 1 (a funnel); sources feed 0, 1
            var cells = new (int, Pos)[] { (0, new Pos(0, 0, 0)), (1, new Pos(0, 2, 0)), (2, new Pos(0, 1, 2)) };
            var feeds = new (int, CellOrSource[])[]
            {
                (2, new CellOrSource[] { 0, 1 }),
                (0, new[] { CellOrSource.FromSource(new SourceId(0)) }),
                (1, new[] { CellOrSource.FromSource(new SourceId(1)) }),
            };
            var b = Custom(cells, new (int, int[])[0], new CellLine[0], feeds,
                new[] { new Source(new SourceId(0), Ids(0)), new Source(new SourceId(1), Ids(1)) });
            var (output, events) = Flow.Settle(b, new Streams(2));
            Assert.That(new[] { 0, 1, 2 }.All(c => output.TileAt(c) != null));
            var m = Of<Moved>(events).Single();
            Assert.That(m.Path[m.Path.Count - 1], Is.EqualTo(new CellId(2)));
            Assert.That(m.Path[0] == 0 || m.Path[0] == 1);
            // fills 0 and 1, then 2 pulls from 0, which refills
            Assert.That(Of<Spawned>(events).Count, Is.EqualTo(3));
        }

        [Test]
        public void Two_layers_drop_into_lower_layer()
        {
            // layer 1 cell 10 drops into layer 0 cell 0 (a layer drop); 10 is fed by a source
            var cells = new (int, Pos)[] { (0, new Pos(0, 0, 0)), (10, new Pos(1, 0, 0)) };
            var feeds = new (int, CellOrSource[])[]
            {
                (0, new CellOrSource[] { 10 }),
                (10, new[] { CellOrSource.FromSource(new SourceId(0)) }),
            };
            var b = Custom(cells, new (int, int[])[0], new CellLine[0], feeds, new[] { new Source(new SourceId(0), Ids(10)) });
            var (output, events) = Flow.Settle(b, new Streams(3));
            Assert.That(output.TileAt(0), Is.Not.Null);
            Assert.That(output.TileAt(10), Is.Not.Null);
            var m = Of<Moved>(events).Single();
            Assert.That(m.Path.ToArray(), Is.EqualTo(Ids(10, 0)));
        }

        [Test]
        public void Sparse_cell_ids_work_with_per_cell_arrays()
        {
            var b = Custom(
                new (int, Pos)[] { (0, new Pos(0, 0, 0)), (10, new Pos(1, 0, 0)) },
                new (int, int[])[0], new CellLine[0],
                new (int, CellOrSource[])[] { (10, new[] { CellOrSource.FromSource(new SourceId(0)) }) },
                new[] { new Source(new SourceId(0), Ids(10)) });
            Assert.That(b.Topology.CellCapacity, Is.EqualTo(11));
            Assert.That(b.Topology.Contains(5), Is.False);
            Assert.That(b.Topology.Cells.Select(c => c.Value).ToArray(), Is.EqualTo(new[] { 0, 10 }));
            Assert.That(b.Flow.Feed(0), Is.Empty);
        }

        [Test]
        public void Settle_with_a_cyclic_flow_throws_instead_of_looping()
        {
            // two cells that each pull from the other, with a tile in one: tiles shuttle forever
            var b = Custom(
                new (int, Pos)[] { (0, new Pos(0, 0, 0)), (1, new Pos(0, 1, 0)) },
                new (int, int[])[0], new CellLine[0],
                new (int, CellOrSource[])[] { (0, new CellOrSource[] { 1 }), (1, new CellOrSource[] { 0 }) },
                new Source[0]);
            b.Put(0, new Tile(b.NewTileId(), 0));
            Assert.Throws<System.InvalidOperationException>(() => Flow.Settle(b, new Streams(1)));
        }

        [Test]
        public void GravityDown_builds_one_source_per_column_and_chains_cells_above_holes()
        {
            var b = BoardFrom(new[] { "0 1 2", ". 2 0", "3 4 1" });
            var f = b.Flow;
            Assert.That(f.Sources.Select(s => s.Id.Value).ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(f.Sources.Select(s => s.Cells.Single().Value).ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
            // (0,2) is fed by (0,0): the hole at (0,1) is skipped
            Assert.That(f.Feed(Cell(b, 0, 2)).Single().Cell, Is.EqualTo(Cell(b, 0, 0)));
            Assert.That(f.Feed(Cell(b, 0, 0)).Single().IsSource, Is.True);
        }
    }
}
