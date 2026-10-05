using System.Linq;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Gameplay
{
    /// <summary>Every tile cleared in a move, chain reactions and special waves included, must count toward the goals.</summary>
    public class ChainCreditTests
    {
        static readonly RuleSet Rules = RuleSets.From(TestDials.Default());

        [Test]
        public void Collected_total_equals_tiles_cleared_in_events()
        {
            int checkedMoves = 0, cascades = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var streams = new Streams(seed);
                var topo = Topology.Grid(7, 7, new (int, int)[0]);
                var board = Moves.NewBoard(topo, Flow.GravityDown(topo), 5, streams);
                var level = new Level(1, 1, 7, 7, new (int, int)[0], 5, new[] { Goal.Collect(1_000_000, 0, 1, 2, 3, 4) }, 1000,
                    1_000_000_000, 1_000_000_001, new[] { "Line", "Bomb", "Color" }, null, 0.5, 0.5, null, 1);
                var s = new Session(level, Rules, null, streams, board);
                int cleared = 0;
                for (int i = 0; i < 40 && s.IsPlaying; i++)
                {
                    var moves = s.LegalMoves;
                    if (moves.Count == 0) break;
                    var m = moves[(i * 7 + (int)seed) % moves.Count];
                    var r = s.TrySwap(m.A, m.B);
                    foreach (var e in r.Events) if (e is Cleared c) cleared += c.TileIds.Count;
                    if (r.Events.OfType<Matched>().Count() > 1) cascades++;
                    checkedMoves++;
                    Assert.That(s.TilesCollected, Is.EqualTo(cleared), "seed " + seed + " move " + i);
                }
            }
            Assert.That(checkedMoves, Is.GreaterThan(100));
            Assert.That(cascades, Is.GreaterThan(0));
        }
    }
}
