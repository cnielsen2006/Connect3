using System.Linq;
using Cascade.Core;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.CoreTests
{
    /// <summary>Tests (TRD §9.1 Core/Moves).</summary>
    public class MovesTests
    {
        [Test]
        public void FindLegal_simple()
        {
            var b = BoardFrom(new[] { "0 1 0", "2 0 3", "1 2 4", "3 4 1" });
            Assert.That(Moves.FindLegal(b), Is.EqualTo(new[] { (Cell(b, 1, 0), Cell(b, 1, 1)) }));
        }

        [Test]
        public void Special_pair_is_legal_even_without_a_match()
        {
            var b = BoardFrom(new[] { "0h 1b 0", "2 3 4", "1 2 3" });
            Assert.That(Moves.FindLegal(b), Does.Contain((Cell(b, 0, 0), Cell(b, 1, 0))));
        }

        [Test]
        public void FindLegal_does_not_mutate()
        {
            var b = BoardFrom(new[] { "0 1 0", "2 0 3", "1 2 4", "3 4 1" });
            var before = Snapshot(b);
            Moves.FindLegal(b);
            Assert.That(Snapshot(b), Is.EqualTo(before));
        }

        [Test]
        public void NewBoard_valid_over_many_seeds()
        {
            var topo = Topology.Grid(8, 8);
            var flow = Flow.GravityDown(topo);
            for (ulong seed = 0; seed < 100; seed++)
            {
                var b = Moves.NewBoard(topo, flow, 5, new Streams(seed));
                Assert.That(Match.FindRuns(b), Is.Empty);
                Assert.That(Moves.FindLegal(b), Is.Not.Empty);
                Assert.That(topo.Cells.All(c => b.TileAt(c) != null));
            }
        }

        [Test]
        public void Shuffle_always_valid_and_keeps_specials()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                var topo = Topology.Grid(6, 6);
                var b = Moves.NewBoard(topo, Flow.GravityDown(topo), 4, new Streams(seed));
                var c0 = b.Topology.Cells[0];
                var t = b.TileAt(c0)!;
                b.Put(c0, new Tile(t.Id, t.Color, new Special.Bomb()));
                var (output, ev) = Moves.Shuffle(b, new Streams(seed));
                Assert.That(Match.FindRuns(output), Is.Empty);
                Assert.That(Moves.FindLegal(output), Is.Not.Empty);
                Assert.That(output.TileAt(c0)!.Special, Is.EqualTo(new Special.Bomb()));
                Assert.That(output.TileAt(c0)!.Id, Is.EqualTo(t.Id));
                Assert.That(ev.Mapping.Count, Is.EqualTo(36)); // colors may be regenerated after 100 failed tries
            }
        }

        [Test]
        public void Shuffle_does_not_touch_its_input()
        {
            var topo = Topology.Grid(5, 5);
            var b = Moves.NewBoard(topo, Flow.GravityDown(topo), 4, new Streams(3));
            var before = Snapshot(b);
            Moves.Shuffle(b, new Streams(3));
            Assert.That(Snapshot(b), Is.EqualTo(before));
        }

        [Test]
        public void NewBoard_is_deterministic_per_seed()
        {
            var topo = Topology.Grid(7, 7);
            var a = Moves.NewBoard(topo, Flow.GravityDown(topo), 4, new Streams(1234));
            var b = Moves.NewBoard(topo, Flow.GravityDown(topo), 4, new Streams(1234));
            Assert.That(Snapshot(a), Is.EqualTo(Snapshot(b)));
        }
    }
}
