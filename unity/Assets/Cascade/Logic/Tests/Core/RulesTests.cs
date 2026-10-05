using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using NUnit.Framework;
using static Cascade.Tests.CoreTests.T;

namespace Cascade.Tests.CoreTests
{
    /// <summary>Tests (TRD §9.1 Core/Rules).</summary>
    public class RulesTests
    {
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

        static readonly (int, int) P22 = (2, 2), P23 = (2, 3);

        [Test]
        public void Rejected_swap_changes_nothing_and_uses_no_move()
        {
            var b = CrossBoard();
            var (output, events) = Play(b, (0, 0), (1, 0));
            Assert.That(events, Is.EqualTo(new Event[] { new SwapRejected(Cell(b, 0, 0), Cell(b, 1, 0)) }));
            Assert.That(b.Topology.Cells.All(c => output.TileAt(c) == b.TileAt(c)));
        }

        [Test]
        public void Input_board_is_not_mutated()
        {
            var b = CrossBoard();
            var before = Snapshot(b);
            Play(b, P22, P23);
            Assert.That(Snapshot(b), Is.EqualTo(before));
        }

        [Test]
        public void Illegal_cells_throw()
        {
            var b = CrossBoard();
            Assert.Throws<System.ArgumentException>(() => Play(b, (0, 0), (2, 0))); // not neighbors
        }

        [Test]
        public void Superset_clearing_and_every_qualifier_scores()
        {
            var b = CrossBoard();
            var (_, events) = Play(b, P22, P23);
            var m = Of<Matched>(events)[0];
            Assert.That(m.Cells.Count, Is.EqualTo(5)); // the L is cleared as one superset
            Assert.That(m.Qualifiers.Select(q => q.Kind).OrderBy(k => k).ToArray(),
                Is.EqualTo(new[] { QualifierKind.Run3, QualifierKind.Run3, QualifierKind.Cross }));
            var firstClear = Of<Cleared>(events)[0];
            Assert.That(firstClear.Wave, Is.EqualTo(0));
            Assert.That(firstClear.TileIds.Count, Is.EqualTo(5));
            Assert.That(Of<Scored>(events)[0], Is.EqualTo(new Scored(2 * 60 + 150, 0)));
            Assert.That(Of<SpecialCreated>(events)[0].Special, Is.EqualTo(new Special.Bomb()));
        }

        [Test]
        public void Run4_earns_a_line_and_run5_earns_a_color()
        {
            var (_, e4) = Play(RunBoard(false), P22, P23);
            Assert.That(Of<Matched>(e4)[0].Qualifiers.ToArray(), Is.EqualTo(new[] { new Qualifier(QualifierKind.Run4, 4) }));
            Assert.That(Of<SpecialCreated>(e4)[0].Special, Is.InstanceOf<Special.Line>());
            Assert.That(Of<Scored>(e4)[0].Points, Is.EqualTo(120));
            var (_, e5) = Play(RunBoard(true), P22, P23);
            Assert.That(Of<Matched>(e5)[0].Qualifiers.ToArray(), Is.EqualTo(new[] { new Qualifier(QualifierKind.Run5, 5) }));
            Assert.That(Of<SpecialCreated>(e5)[0].Special, Is.InstanceOf<Special.Color>());
            Assert.That(Of<Scored>(e5)[0].Points, Is.EqualTo(200));
        }

        [Test]
        public void Special_host_pools_follow_incoming_chance()
        {
            var b = RunBoard(false);
            int n = b.NextTileId;
            var d0 = new RuleSet { Specials = new SpecialRules { IncomingChance = 0.0 } };
            var (_, e) = Play(b, P22, P23, rules: d0);
            Assert.That(Of<SpecialCreated>(e)[0].TileId, Is.LessThan(n)); // an existing board tile
            var d1 = new RuleSet { Specials = new SpecialRules { IncomingChance = 1.0 } };
            (_, e) = Play(b, P22, P23, rules: d1);
            int host = Of<SpecialCreated>(e)[0].TileId;
            Assert.That(host, Is.GreaterThanOrEqualTo(n));
            Assert.That(Of<Spawned>(e).Select(x => x.TileId), Does.Contain(host));
        }

        [Test]
        public void Special_chain_bomb_then_line_then_color()
        {
            var b = BoardFrom(new[] { "1 2 3 4 1", "2 3 4 1 2", "0 0b 1 2 3", "3h 4 0 1 2c", "4 1 2 3 4" });
            int bomb = b.TileAt(Cell(b, 1, 2))!.Id, line = b.TileAt(Cell(b, 0, 3))!.Id, color = b.TileAt(Cell(b, 4, 3))!.Id;
            var (_, events) = Play(b, P22, P23);
            var fired = Of<SpecialFired>(events).Select(e => e.TileId).ToArray();
            Assert.That(fired, Is.EqualTo(new[] { bomb, line, color }));
            var clears = Of<Cleared>(events);
            Assert.That(clears.Take(4).Select(e => e.Wave).ToArray(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            var cleared = clears.Take(4).SelectMany(e => e.TileIds).ToList();
            Assert.That(cleared.Distinct().Count(), Is.EqualTo(cleared.Count)); // nothing is cleared (or fires) twice
        }

        [Test]
        public void Special_swap_is_legal_without_a_match_and_both_fire()
        {
            var b = BoardFrom(new[] { "0h 1b 3 4 1", "2 3 4 1 2", "0 2 1 3 4", "3 4 0 1 2", "4 1 2 3 0" });
            var ids = new[] { b.TileAt(Cell(b, 0, 0))!.Id, b.TileAt(Cell(b, 1, 0))!.Id };
            var (_, events) = Play(b, (0, 0), (1, 0));
            Assert.That(events[0], Is.InstanceOf<Swapped>());
            Assert.That(Of<SpecialFired>(events).Select(e => e.TileId).OrderBy(i => i).ToArray(), Is.EqualTo(ids.OrderBy(i => i).ToArray()));
            Assert.That(Of<Cleared>(events)[0].TileIds.Count, Is.EqualTo(2));
            Assert.That(Of<Cleared>(events).Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Color_plus_color_clears_the_whole_board()
        {
            var b = BoardFrom(new[] { "0c 1c 3 4 1", "2 3 4 1 2", "0 2 1 3 4", "3 4 0 1 2", "4 1 2 3 0" });
            var (output, events) = Play(b, (0, 0), (1, 0));
            Assert.That(Of<Scored>(events)[0], Is.EqualTo(new Scored(5000, 0))); // the combo dial; all tiles go in wave 0
            Assert.That(Of<Cleared>(events)[0].TileIds.Count, Is.EqualTo(25));
            Assert.That(Of<Spawned>(events).Count, Is.GreaterThanOrEqualTo(25));
            Assert.That(Match.FindRuns(output), Is.Empty);
        }

        [Test]
        public void Color_special_uses_the_color_it_was_swapped_with()
        {
            var b = BoardFrom(new[] { "1 2 3 4 1", "2 3 4 1 2", "0 0 1 2 3", "3 4 0c 1 2", "4 1 2 3 4" });
            var (_, events) = Play(b, P22, P23);
            var fire = Of<SpecialFired>(events)[0];
            var after = b.Copy();
            after.Swap(Cell(b, 2, 2), Cell(b, 2, 3));
            var want = b.Topology.Cells.Where(c => after.TileAt(c)!.Color == 1 || c == Cell(b, 2, 2)).Select(c => c.Value).ToArray();
            Assert.That(fire.Cells.Select(c => c.Value).ToArray(), Is.EqualTo(want)); // both ascending
        }

        [Test]
        public void Several_groups_in_one_round_score_at_a_rising_multiplier()
        {
            // one swap completes a vertical run of 1s in column 1 and a vertical run of 0s in column 2
            var b = BoardFrom(new[] { "2 1 0 3 4", "3 1 0 4 2", "4 0 1 2 3", "0 3 2 1 4", "1 4 3 0 2" });
            var (_, events) = Play(b, (1, 2), (2, 2));
            var matched = Of<Matched>(events);
            Assert.That(matched.Count, Is.GreaterThanOrEqualTo(2));
            var scored = Of<Scored>(events);
            Assert.That(scored[0], Is.EqualTo(new Scored(60, 0, 1.0)));
            Assert.That(scored[1], Is.EqualTo(new Scored(75, 0, 1.25))); // 60 * (1 + group_step)
            Assert.That(scored.Where(e => e.Depth == 0).Count(), Is.EqualTo(2));
            Assert.That(events.TakeWhile(e => !(e is Scored)).Last(), Is.TypeOf<SpecialFired>().Or.TypeOf<Cleared>(), "group scores follow the clears");
        }

        [Test]
        public void A_start_level_raises_every_multiplier()
        {
            var b = BoardFrom(new[] { "2 1 0 3 4", "3 1 0 4 2", "4 0 1 2 3", "0 3 2 1 4", "1 4 3 0 2" });
            var streams = new Streams(1);
            var (_, events) = Rules.Resolve(b, Cell(b, 1, 2), Cell(b, 2, 2), streams, RuleSet.Default, startLevel: 2);
            Assert.That(Of<Scored>(events)[0], Is.EqualTo(new Scored(120, 0, 2.0))); // 1 + 0.5 * 2
        }

        [Test]
        public void Cascade_multiplier()
        {
            Assert.That(Scoring.Points(100, 0, DefaultRules.Scoring), Is.EqualTo(100));
            Assert.That(Scoring.Points(100, 2, DefaultRules.Scoring), Is.EqualTo(200)); // 1 + 0.5 * 2
        }

        [Test]
        public void Cascades_happen_and_score_with_depth()
        {
            var topo = Topology.Grid(8, 8);
            var flow = Flow.GravityDown(topo);
            var depths = new HashSet<int>();
            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = new Streams(seed);
                var b = Moves.NewBoard(topo, flow, 5, s);
                for (int i = 0; i < 10; i++)
                {
                    var mv = s.Bot.Choice(Moves.FindLegal(b));
                    var (nb, events) = Core.Rules.Resolve(b, mv, s, DefaultRules);
                    b = nb;
                    foreach (var sc in Of<Scored>(events)) depths.Add(sc.Depth);
                }
            }
            Assert.That(depths.Max(), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Invariants_over_random_sessions()
        {
            var topo = Topology.Grid(8, 8);
            var flow = Flow.GravityDown(topo);
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = new Streams(seed);
                var b = Moves.NewBoard(topo, flow, 5, s);
                for (int i = 0; i < 15; i++)
                {
                    var mv = s.Bot.Choice(Moves.FindLegal(b));
                    var (nb, events) = Core.Rules.Resolve(b, mv, s, DefaultRules);
                    b = nb;
                    Assert.That(Match.FindRuns(b), Is.Empty);
                    Assert.That(Moves.FindLegal(b), Is.Not.Empty);
                    var tiles = topo.Cells.Select(c => b.TileAt(c)).ToList();
                    Assert.That(tiles.All(t => t != null));
                    Assert.That(tiles.Select(t => t!.Id).Distinct().Count(), Is.EqualTo(64));
                    Assert.That(Events.FromJson(Events.ToJson(events)), Is.EqualTo(events));
                }
            }
        }

        [Test]
        public void Determinism()
        {
            List<Event> Session()
            {
                var s = new Streams(99);
                var topo = Topology.Grid(8, 8);
                var b = Moves.NewBoard(topo, Flow.GravityDown(topo), 5, s);
                var log = new List<Event>();
                for (int i = 0; i < 10; i++)
                {
                    var mv = s.Bot.Choice(Moves.FindLegal(b));
                    var (nb, e) = Core.Rules.Resolve(b, mv, s, DefaultRules);
                    b = nb;
                    log.AddRange(e);
                }
                return log;
            }
            Assert.That(Session(), Is.EqualTo(Session()));
        }

        [Test]
        public void Shuffle_event_when_no_legal_move_remains()
        {
            bool found = false;
            var topo = Topology.Grid(5, 5);
            var flow = Flow.GravityDown(topo);
            for (ulong seed = 0; seed < 300 && !found; seed++)
            {
                var s = new Streams(seed);
                var b = Moves.NewBoard(topo, flow, 6, s);
                for (int i = 0; i < 8; i++)
                {
                    var mv = s.Bot.Choice(Moves.FindLegal(b));
                    var (nb, events) = Core.Rules.Resolve(b, mv, s, DefaultRules);
                    b = nb;
                    if (Of<Shuffled>(events).Count > 0)
                    {
                        found = true;
                        Assert.That(Moves.FindLegal(b), Is.Not.Empty);
                        Assert.That(Match.FindRuns(b), Is.Empty);
                    }
                }
            }
            Assert.That(found, Is.True);
        }

        // --- extras -----------------------------------------------------------------------------

        [Test]
        public void Specials_enabled_filters_earned_specials_but_still_scores()
        {
            var b = RunBoard(true);
            b.SpecialsEnabled = new[] { "Line" };
            var (_, events) = Play(b, P22, P23);
            Assert.That(Of<SpecialCreated>(events), Is.Empty);
            Assert.That(Of<Scored>(events).Any(e => e.Points >= 200));
            b.SpecialsEnabled = new string[0];
            Assert.That(Of<SpecialCreated>(Play(b, P22, P23).Events), Is.Empty);
            Assert.That(b.Copy().SpecialsEnabled, Is.Empty); // kept by Copy
        }

        [Test]
        public void Resolve_caches_the_legal_moves_of_the_result()
        {
            var (output, _) = Play(CrossBoard(), P22, P23);
            Assert.That(output.LegalCache, Is.Not.Null);
            Assert.That(output.LegalCache, Is.EqualTo(Moves.FindLegal(output)));
            output.Put(output.Topology.Cells[0], output.TileAt(output.Topology.Cells[0]));
            Assert.That(output.LegalCache, Is.Null);
        }

        [Test]
        public void RuleSet_defaults_equal_the_shipped_dials()
        {
            var r = new RuleSet();
            Assert.That(r.Scoring, Is.EqualTo(new ScoringRules { Run3 = 60, Run4 = 120, Run5 = 200, RunExtraPerTile = 40, Cross = 150, SpecialTileCleared = 20, CascadeStep = 0.5, GroupStep = 0.25, ComboColorColor = 5000 }));
            Assert.That(r.Specials, Is.EqualTo(new SpecialRules { IncomingChance = 0.4, BombRadius = 1 }));
            Assert.That(r, Is.EqualTo(RuleSet.Default));
        }

        [Test]
        public void Scoring_run_extra_and_special_cleared()
        {
            var s = new ScoringRules();
            Assert.That(Scoring.Base(new[] { new Qualifier(QualifierKind.Run5, 7) }, s), Is.EqualTo(200 + 2 * 40));
            Assert.That(Scoring.SpecialCleared(3, s), Is.EqualTo(60));
            Assert.That(Scoring.Score(new[] { new Qualifier(QualifierKind.Run3, 3), new Qualifier(QualifierKind.Cross) }, 1.5, s), Is.EqualTo(315)); // (60 + 150) * 1.5
            Assert.That(Scoring.Points(5, 1, new ScoringRules { CascadeStep = 0.5 }), Is.EqualTo(8)); // 7.5 -> 8 (half to even)
            Assert.That(Scoring.Points(3, 1, new ScoringRules { CascadeStep = 0.5 }), Is.EqualTo(4)); // 4.5 -> 4
        }

        [Test]
        public void Bomb_radius_comes_from_the_rule_set()
        {
            var b = BoardFrom(new[] { "0 1 2 3 4", "1 2 3 4 0", "2 3 4b 0 1", "3 4 0 1 2", "4 0 1 2 3" });
            var cell = Cell(b, 2, 2);
            var tile = b.TileAt(cell)!;
            Assert.That(Specials.Fire(b, cell, tile, new SpecialRules { BombRadius = 1 }).Count, Is.EqualTo(9));
            Assert.That(Specials.Fire(b, cell, tile, new SpecialRules { BombRadius = 2 }).Count, Is.EqualTo(25));
            Assert.That(Specials.Fire(b, cell, tile, new SpecialRules { BombRadius = 0 }).Count, Is.EqualTo(1));
        }

        [Test]
        public void Color_special_without_partner_fires_on_the_most_common_color_lowest_id_on_ties()
        {
            var b = BoardFrom(new[] { "0 1 2", "1 0c 2", "2 0 1" }); // 0:3 (incl. the Color tile), 1:3, 2:3 -> tie -> color 0
            var cell = Cell(b, 1, 1);
            var hit = Specials.Fire(b, cell, b.TileAt(cell)!, new SpecialRules());
            Assert.That(hit.All(c => c == cell || b.TileAt(c)!.Color == 0));
            Assert.That(hit.Count, Is.EqualTo(3));
            Assert.That(Specials.MostCommonColor(b), Is.EqualTo(0));
            // a partner color that no longer exists falls back to the most common color
            var hit2 = Specials.Fire(b, cell, b.TileAt(cell)!, new SpecialRules(), partnerColor: 4);
            Assert.That(hit2, Is.EqualTo(hit));
        }

        [Test]
        public void Line_special_clears_its_row_or_column()
        {
            var b = BoardFrom(new[] { "0 1 2 3", "1 2h 3 0", "2 3 0 1" });
            var cell = Cell(b, 1, 1);
            var row = Specials.Fire(b, cell, b.TileAt(cell)!, new SpecialRules());
            Assert.That(row.Select(c => b.Topology.Pos(c).Y).Distinct().ToArray(), Is.EqualTo(new[] { 1 }));
            Assert.That(row.Count, Is.EqualTo(4));
        }
    }
}
