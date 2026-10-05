using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Presentation;
using Cascade.Tests.Conformance;
using Cascade.Tests.CoreTests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>
    /// TRD 9.1, Presentation/Timeline: the golden event logs (conformance/golden/session_*.json) and Core-resolved
    /// special boards go through the planner; structural invariants must hold for every move.
    /// </summary>
    public class TimelineGoldenTests
    {
        public static IEnumerable<int> GoldenSeeds() => new[] { 1, 2, 3 };
        static readonly double[] Speeds = { 0.5, 1.0, 1.5, 2.0, 3.0 };

        /// <summary>One resolved move: the board before, the events, the board after.</summary>
        sealed class Move
        {
            public Board Before = null!;
            public List<Event> Events = null!;
            public Board After = null!;
            public string Label = "";
        }

        /// <summary>Replays a golden session like the Core conformance test; the events are parsed from the fixture itself.</summary>
        static List<Move> GoldenMoves(int seed)
        {
            var fixture = (JArray)ConformanceFiles.ReadJson("golden", $"session_{seed}.json");
            var s = new Streams((ulong)seed);
            var topo = Topology.Grid(8, 8);
            var board = Moves.NewBoard(topo, Flow.GravityDown(topo), 5, s);
            var list = new List<Move>();
            int i = 0;
            foreach (var step in fixture)
            {
                var mv = s.Bot.Choice(Moves.FindLegal(board));
                var (next, events) = Core.Rules.Resolve(board, mv, s, RuleSet.Default);
                var parsed = Events.FromJson(step["events"]!.ToString());
                Assert.That(Events.ToJson(parsed), Is.EqualTo(Events.ToJson(events)), "fixture and replay agree");
                list.Add(new Move { Before = board, Events = parsed, After = next, Label = $"session {seed} move {i++}" });
                board = next;
            }
            return list;
        }

        static string[] PatternRows(int w, int h, Func<int, int, string> token) =>
            Enumerable.Range(0, h).Select(y => string.Join(" ", Enumerable.Range(0, w).Select(x => token(x, y)))).ToArray();

        /// <summary>Boards with specials, a hole and a shuffle-free layout, resolved by Core, so SpecialFired / SpecialCreated appear.</summary>
        static List<Move> SpecialMoves()
        {
            var list = new List<Move>();
            // no initial match: color = (x + 2y) % 5
            string Plain(int x, int y) => ((x + 2 * y) % 5).ToString();

            void Add(string label, Board b, (int, int) a, (int, int) c, ulong seed)
            {
                var (after, events) = T.Play(b, a, c, seed);
                list.Add(new Move { Before = b, Events = events, After = after, Label = label });
            }

            // bomb + line swapped: both fire
            Add("bomb+line", T.BoardFrom(PatternRows(6, 6, (x, y) => (x, y) == (2, 2) ? "0b" : (x, y) == (3, 2) ? "1h" : Plain(x, y))), (2, 2), (3, 2), 5);
            // color + plain swapped is only legal with a match; use two specials instead: color + line
            Add("color+line", T.BoardFrom(PatternRows(6, 6, (x, y) => (x, y) == (2, 3) ? "0c" : (x, y) == (3, 3) ? "1v" : Plain(x, y))), (2, 3), (3, 3), 6);
            // color + color clears the board
            Add("color+color", T.BoardFrom(PatternRows(5, 5, (x, y) => (x, y) == (1, 1) ? "0c" : (x, y) == (2, 1) ? "1c" : Plain(x, y))), (1, 1), (2, 1), 7);
            // a bomb next to a hole
            Add("bomb with a hole", T.BoardFrom(PatternRows(6, 6, (x, y) => (x, y) == (3, 3) ? "." : (x, y) == (2, 2) ? "0b" : (x, y) == (2, 3) ? "1h" : Plain(x, y))), (2, 2), (2, 3), 8);
            return list;
        }

        static IEnumerable<Move> AllMoves()
        {
            foreach (int seed in GoldenSeeds()) foreach (var m in GoldenMoves(seed)) yield return m;
            foreach (var m in SpecialMoves()) yield return m;
        }

        static TimingParams At(double speed) => TimingParams.Default with { Speed = speed };

        static Timeline PlanOf(Move m, double speed = 1.0)
            => TimelinePlanner.Plan(m.Before, m.Events, At(speed), BoardGeometry.FromTopology(m.Before.Topology));

        static HashSet<int> TileIds(Board b) => new HashSet<int>(b.Topology.Cells.Select(c => b.TileAt(c)).Where(t => t != null).Select(t => t!.Id));

        [Test]
        public void Special_boards_really_exercise_special_events()
        {
            var kinds = SpecialMoves().SelectMany(m => m.Events).Select(e => e.TypeName).ToHashSet();
            Assert.That(kinds, Does.Contain("SpecialFired"));
            var washes = SpecialMoves().SelectMany(m => PlanOf(m).Of<ShowFx>()).Select(f => f.Kind).ToHashSet();
            Assert.That(washes, Is.SupersetOf(new[] { FxKind.LineWash, FxKind.BombWash, FxKind.ColorWash, FxKind.Ripple, FxKind.ScorePopup }));
        }

        [Test]
        public void Planning_is_consistent_with_the_events_no_warnings()
        {
            foreach (var m in AllMoves()) Assert.That(PlanOf(m).Warnings, Is.Empty, m.Label);
        }

        [Test]
        public void Every_spawned_moved_and_cleared_tile_is_covered()
        {
            foreach (var m in AllMoves())
            {
                var tl = PlanOf(m);
                var spawns = tl.Of<SpawnPiece>().Select(s => s.TileId).ToList();
                var removes = tl.Of<RemovePiece>().Select(s => s.TileId).ToList();
                var positionTweens = tl.Of<Tween>().Where(t => t.Property == PoseProperty.Position).Select(t => t.TileId).ToHashSet();
                var sinks = tl.Of<Tween>().Where(t => t.Property == PoseProperty.Sink).Select(t => t.TileId).ToList();
                foreach (var e in m.Events)
                    switch (e)
                    {
                        case Spawned s:
                            Assert.That(spawns, Does.Contain(s.TileId), m.Label);
                            Assert.That(positionTweens, Does.Contain(s.TileId), m.Label);
                            break;
                        case Moved mv:
                            Assert.That(positionTweens, Does.Contain(mv.TileId), m.Label);
                            Assert.That(tl.Of<Tween>().Any(t => t.TileId == mv.TileId && t.Path != null && t.Path.Count == mv.Path.Count), m.Label);
                            break;
                        case Cleared c:
                            foreach (int id in c.TileIds)
                            {
                                Assert.That(removes, Does.Contain(id), m.Label);
                                Assert.That(sinks, Does.Contain(id), m.Label);
                            }
                            break;
                    }
                Assert.That(spawns.Distinct().Count(), Is.EqualTo(spawns.Count), m.Label + ": a tile spawns once");
                Assert.That(removes.Distinct().Count(), Is.EqualTo(removes.Count), m.Label + ": a tile is removed once");
            }
        }

        [Test]
        public void Applying_the_instants_leaves_exactly_the_tiles_of_the_final_board()
        {
            foreach (var m in AllMoves())
            {
                var tl = PlanOf(m);
                var live = TileIds(m.Before);
                foreach (var it in tl.Items)
                {
                    if (it is SpawnPiece s) Assert.That(live.Add(s.TileId), Is.True, m.Label);
                    if (it is RemovePiece r) Assert.That(live.Remove(r.TileId), Is.True, m.Label);
                }
                Assert.That(live, Is.EquivalentTo(TileIds(m.After)), m.Label);
            }
        }

        [Test]
        public void At_the_end_every_driven_piece_rests_on_its_final_cell_and_pose_is_reset()
        {
            foreach (var m in AllMoves())
            {
                var layout = BoardGeometry.FromTopology(m.After.Topology);
                var tl = PlanOf(m);
                var frame = new TimelineSampler(tl).Advance(tl.EndMs + 1);
                Assert.That(frame.Settled && frame.Finished, Is.True);
                var final = new Dictionary<int, CellId>();
                foreach (var c in m.After.Topology.Cells) { var t = m.After.TileAt(c); if (t != null) final[t.Id] = c; }
                var before = new Dictionary<int, CellId>();
                foreach (var c in m.Before.Topology.Cells) { var t = m.Before.TileAt(c); if (t != null) before[t.Id] = c; }

                var driven = new HashSet<int>();
                foreach (var p in frame.Poses.Where(p => p.Property == PoseProperty.Position))
                {
                    driven.Add(p.TileId);
                    Assert.That(final.ContainsKey(p.TileId), m.Label + ": driven tile " + p.TileId + " survives");
                    Assert.That(p.Value.Near(layout.CellPosition(final[p.TileId]), 1e-9), Is.True,
                        $"{m.Label}: tile {p.TileId} ends at {p.Value}, wanted {layout.CellPosition(final[p.TileId])}");
                }
                foreach (var kv in final.Where(kv => !driven.Contains(kv.Key)))
                    Assert.That(before.TryGetValue(kv.Key, out var was) && was == kv.Value, Is.True, $"{m.Label}: undriven tile {kv.Key} must not have moved");

                foreach (var p in frame.Poses)
                {
                    if (p.Property == PoseProperty.Squash) Assert.That(p.Scalar, Is.EqualTo(1.0).Within(1e-9), m.Label);
                    if (p.Property == PoseProperty.Roll || p.Property == PoseProperty.Hover) Assert.That(p.Scalar, Is.EqualTo(0.0).Within(1e-9), m.Label);
                    if (p.Property == PoseProperty.Sink) Assert.Fail(m.Label + ": a sunk tile must have been removed");
                }
            }
        }

        [Test]
        public void Durations_match_the_formulas_at_every_speed()
        {
            foreach (var m in AllMoves())
                foreach (double speed in Speeds)
                {
                    var tl = PlanOf(m, speed);
                    var t = At(speed);
                    foreach (var tw in tl.Of<Tween>())
                    {
                        if (tw.Property == PoseProperty.Sink) Assert.That(tw.DurationMs, Is.EqualTo(250 / speed).Within(1e-9), m.Label);
                        if (tw.Property == PoseProperty.Position && tw.Path != null)
                            Assert.That(tw.DurationMs, Is.EqualTo((tw.Path.Count - 1) / (14 * speed) * 1000).Within(1e-9), m.Label);
                        if (tw.Property == PoseProperty.Position && tw.Path == null && tw.Easing == Easing.SmoothStep)
                            Assert.That(tw.DurationMs, Is.EqualTo(180 / speed).Within(1e-9), m.Label);
                    }
                    foreach (var s in tl.Of<SpawnPiece>())
                    {
                        var drop = tl.Of<Tween>().Single(x => x.TileId == s.TileId && x.Property == PoseProperty.Position && x.Path == null);
                        double height = drop.From.Y - drop.To.Y;
                        Assert.That(drop.DurationMs, Is.EqualTo(160 / speed * height / 1.6).Within(1e-9), m.Label);
                    }
                    Assert.That(t.Speed, Is.EqualTo(speed));
                }
        }

        [Test]
        public void Total_duration_shrinks_with_animation_speed_in_proportion()
        {
            foreach (var m in AllMoves())
            {
                double baseline = PlanOf(m, 1.0).TotalMs;
                Assert.That(baseline, Is.GreaterThan(0), m.Label);
                double previous = double.MaxValue;
                foreach (double speed in Speeds)
                {
                    var tl = PlanOf(m, speed);
                    Assert.That(tl.TotalMs, Is.LessThanOrEqualTo(previous), m.Label + " monotone in speed");
                    Assert.That(tl.TotalMs, Is.EqualTo(baseline / speed).Within(1e-6), m.Label);
                    Assert.That(tl.EndMs, Is.GreaterThanOrEqualTo(tl.TotalMs));
                    Assert.That(tl.Items.Last(), Is.TypeOf<SyncToFinalBoard>());
                    Assert.That(tl.Items.Last().StartMs, Is.EqualTo(tl.TotalMs));
                    previous = tl.TotalMs;
                }
            }
        }

        [Test]
        public void Spawns_into_one_cell_in_one_round_stack_one_cell_higher_each()
        {
            foreach (var m in AllMoves())
            {
                var layout = BoardGeometry.FromTopology(m.Before.Topology);
                var tl = PlanOf(m);
                // group the Spawned events of one round (between Cleared events) by entry cell
                var round = new Dictionary<int, int>();
                foreach (var e in m.Events)
                {
                    if (e is Cleared) round.Clear();
                    if (e is Spawned s)
                    {
                        round.TryGetValue(s.Cell.Value, out int k);
                        round[s.Cell.Value] = k + 1;
                        var sp = tl.Of<SpawnPiece>().Single(x => x.TileId == s.TileId);
                        Assert.That(sp.Position.Y - layout.CellPosition(s.Cell).Y, Is.EqualTo(1.6 + k).Within(1e-9), m.Label);
                    }
                }
            }
        }

        [Test]
        public void Pieces_in_a_column_never_overlap_while_falling_and_landing()
        {
            foreach (var m in AllMoves())
            {
                var tl = PlanOf(m);
                double from = m.Events.Count > 0 && m.Events[0] is Swapped ? 180 : 0; // a vertical swap legitimately crosses two pieces
                var sampler = new TimelineSampler(tl);
                var pos = new Dictionary<int, Float2>();
                foreach (var c in m.Before.Topology.Cells)
                {
                    var t = m.Before.TileAt(c);
                    if (t != null) pos[t.Id] = BoardGeometry.FromTopology(m.Before.Topology).CellPosition(c);
                }
                for (double t = 0; t <= tl.TotalMs; t += 5)
                {
                    var f = sampler.Advance(t);
                    foreach (var inst in f.Fired)
                    {
                        if (inst is SpawnPiece sp) pos[sp.TileId] = sp.Position;
                        if (inst is RemovePiece rp) pos.Remove(rp.TileId);
                    }
                    foreach (var p in f.Poses.Where(p => p.Property == PoseProperty.Position)) pos[p.TileId] = p.Value;
                    if (t < from) continue;
                    var sunk = f.Poses.Where(p => p.Property == PoseProperty.Sink && p.Scalar > 0).Select(p => p.TileId).ToHashSet();
                    var live = pos.Where(kv => !sunk.Contains(kv.Key)).Select(kv => (Id: kv.Key, P: kv.Value)).OrderBy(x => x.Id).ToList();
                    for (int i = 0; i < live.Count; i++)
                        for (int j = i + 1; j < live.Count; j++)
                            if (Math.Abs(live[i].P.X - live[j].P.X) < 1e-6)
                                Assert.That(Math.Abs(live[i].P.Y - live[j].P.Y), Is.GreaterThan(0.45),
                                    $"{m.Label} t={t}: tiles {live[i].Id} and {live[j].Id} overlap at {live[i].P} / {live[j].P}");
                }
            }
        }

        [Test]
        public void Golden_logs_produce_a_timeline_for_every_move()
        {
            foreach (int seed in GoldenSeeds())
            {
                var moves = GoldenMoves(seed);
                Assert.That(moves.Count, Is.EqualTo(12));
                foreach (var m in moves)
                {
                    var tl = PlanOf(m);
                    Assert.That(tl.Of<ScoreApplied>().Sum(s => s.Points), Is.EqualTo(m.Events.OfType<Scored>().Sum(s => s.Points)), m.Label);
                    Assert.That(tl.Of<Tween>().Any(t => t.Property == PoseProperty.Position), m.Label);
                }
            }
        }
    }
}
