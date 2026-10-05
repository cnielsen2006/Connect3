using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Presentation;
using Cascade.Tests.CoreTests;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 9.1, Presentation/Timeline: hand-checked timelines (the formulas of TRD 12.3 with the shipped dials).</summary>
    public class TimelinePlannerTests
    {
        const double Eps = 1e-9;
        static readonly TimingParams Timing = TimingParams.Default; // swap 180, clear 250, fire 300, fall 14/s, spawn 160, stagger 60

        // 3 x 3 board, tile id = cell id = row * 3 + column; tile 0 is a horizontal Line special.
        static Board Board3(string[]? rows = null) => T.BoardFrom(rows ?? new[] { "0h 1 2", "1 0 2", "0 1 2" });

        static Timeline Plan(Board b, TimingParams? t, params Event[] events)
            => TimelinePlanner.Plan(b, events, t ?? Timing, BoardGeometry.FromTopology(b.Topology));

        static Tween[] Tw(Timeline t, int tile, PoseProperty p) => t.Of<Tween>().Where(x => x.TileId == tile && x.Property == p).ToArray();
        static Tween One(Timeline t, int tile, PoseProperty p)
        {
            var all = Tw(t, tile, p);
            Assert.That(all.Length, Is.EqualTo(1), "tweens of tile " + tile + " " + p);
            return all[0];
        }

        static CellId[] C(params int[] ids) => ids.Select(i => new CellId(i)).ToArray();

        [Test]
        public void Empty_event_list_is_just_the_sync_marker()
        {
            var b = Board3();
            var tl = Plan(b, null);
            Assert.That(tl.TotalMs, Is.EqualTo(0));
            Assert.That(tl.Items.Count, Is.EqualTo(1));
            Assert.That(tl.Items[0], Is.TypeOf<SyncToFinalBoard>());
        }

        [Test]
        public void Swap_slides_both_pieces_first_with_a_rock_in_opposite_directions()
        {
            var b = Board3();
            var tl = Plan(b, null, new Swapped(3, 4));
            var g = BoardGeometry.FromTopology(b.Topology);
            var a = One(tl, 3, PoseProperty.Position);
            Assert.That(a.StartMs, Is.EqualTo(0));
            Assert.That(a.DurationMs, Is.EqualTo(180).Within(Eps));
            Assert.That(a.From, Is.EqualTo(g.CellPosition(3)));
            Assert.That(a.To, Is.EqualTo(g.CellPosition(4)));
            Assert.That(One(tl, 4, PoseProperty.Position).To, Is.EqualTo(g.CellPosition(3)));
            Assert.That(One(tl, 3, PoseProperty.Roll).To.X, Is.EqualTo(7).Within(Eps));
            Assert.That(One(tl, 4, PoseProperty.Roll).To.X, Is.EqualTo(-7).Within(Eps));
            Assert.That(One(tl, 3, PoseProperty.Hover).Easing, Is.EqualTo(Easing.Bump));
            Assert.That(tl.TotalMs, Is.EqualTo(180).Within(Eps));
            Assert.That(tl.Items[tl.Items.Count - 1], Is.TypeOf<SyncToFinalBoard>());
            Assert.That(tl.Items[tl.Items.Count - 1].StartMs, Is.EqualTo(180).Within(Eps));
            Assert.That(tl.Warnings, Is.Empty);
        }

        [Test]
        public void Rejected_swap_nudges_toward_the_partner_and_returns()
        {
            var b = Board3();
            var tl = Plan(b, null, new SwapRejected(0, 1));
            var g = BoardGeometry.FromTopology(b.Topology);
            var n = One(tl, 0, PoseProperty.Position);
            Assert.That(n.DurationMs, Is.EqualTo(220).Within(Eps));
            Assert.That(n.Easing, Is.EqualTo(Easing.BumpPow15));
            Assert.That(n.To.X, Is.EqualTo(g.CellPosition(0).X + 0.38).Within(Eps));
            Assert.That(n.ValueAt(220).X, Is.EqualTo(g.CellPosition(0).X).Within(Eps), "back home at the end");
            Assert.That(tl.TotalMs, Is.EqualTo(220).Within(Eps));
        }

        [Test]
        public void Match_clear_fall_and_spawn_follow_the_reference_timeline_exactly()
        {
            var b = Board3();
            // swap (0..180); clear of the top row at 180..430 with a score popup at 180 + 0.3 * 250; then tile 4 (now at
            // cell 3) falls into cell 0 and two new pieces drop into cell 0
            var tl = Plan(b, null,
                new Swapped(3, 4),
                new Matched(C(0, 1, 2), new Qualifier[] { new Qualifier(QualifierKind.Run3, 3) }),
                new Cleared(new[] { 0, 1, 2 }, 0),
                new Scored(60, 0),
                new Spawned(10, 0, 0, 1),
                new Spawned(11, 0, 0, 2),
                new Moved(4, C(3, 0)));

            var g = BoardGeometry.FromTopology(b.Topology);
            // clears
            foreach (int tile in new[] { 0, 1, 2 })
            {
                var sink = One(tl, tile, PoseProperty.Sink);
                Assert.That(sink.StartMs, Is.EqualTo(180).Within(Eps));
                Assert.That(sink.DurationMs, Is.EqualTo(250).Within(Eps));
                Assert.That(sink.Easing, Is.EqualTo(Easing.QuadIn));
            }
            var removes = tl.Of<RemovePiece>().ToArray();
            Assert.That(removes.Select(r => r.TileId), Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(removes.All(r => Math.Abs(r.StartMs - 430) < Eps));
            var ripples = tl.Of<ShowFx>().Where(f => f.Kind == FxKind.Ripple).ToArray();
            Assert.That(ripples.Length, Is.EqualTo(3));
            Assert.That(ripples.Select(r => r.Position), Is.EquivalentTo(new[] { g.CellPosition(0), g.CellPosition(1), g.CellPosition(2) }));

            // score: callback and popup at the centre of the match (0, +1), 75 ms into the clear
            var score = tl.Of<ScoreApplied>().Single();
            Assert.That((score.Points, score.Depth), Is.EqualTo((60, 0)));
            Assert.That(score.StartMs, Is.EqualTo(255).Within(Eps));
            var popup = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.ScorePopup);
            Assert.That(popup.Text, Is.EqualTo("+60"));
            Assert.That(popup.Position, Is.EqualTo(new Float2(0, 1)));
            Assert.That(popup.DurationMs, Is.EqualTo(900).Within(Eps));

            // spawns start when the clears have finished (430); the second one a cell higher and slower
            var s1 = tl.Of<SpawnPiece>().Single(s => s.TileId == 10);
            var s2 = tl.Of<SpawnPiece>().Single(s => s.TileId == 11);
            Assert.That(s1.StartMs, Is.EqualTo(430).Within(Eps));
            Assert.That(s1.Position, Is.EqualTo(g.AbovePosition(0, 1.6)));
            Assert.That(s2.Position, Is.EqualTo(g.AbovePosition(0, 2.6)));
            var d1 = One(tl, 10, PoseProperty.Position);
            var d2 = One(tl, 11, PoseProperty.Position);
            Assert.That(d1.DurationMs, Is.EqualTo(160).Within(Eps));
            Assert.That(d2.DurationMs, Is.EqualTo(160 * 2.6 / 1.6).Within(Eps));
            Assert.That(d1.To, Is.EqualTo(g.CellPosition(0)));
            Assert.That(d1.Easing, Is.EqualTo(Easing.FallAccel));

            // the fall: one cell at 14 cells/s, then the landing squash and wobble
            double fall = 1000.0 / 14;
            var move = Tw(tl, 4, PoseProperty.Position).Last();
            Assert.That(move.StartMs, Is.EqualTo(430).Within(Eps));
            Assert.That(move.DurationMs, Is.EqualTo(fall).Within(Eps));
            Assert.That(move.Path!.Count, Is.EqualTo(2));
            var squash = One(tl, 4, PoseProperty.Squash);
            Assert.That(squash.StartMs, Is.EqualTo(430 + fall).Within(Eps));
            Assert.That(squash.DurationMs, Is.EqualTo(320).Within(Eps), "220 + 100 * wobbles");
            Assert.That(squash.From.X, Is.EqualTo(1));
            Assert.That(squash.To.X, Is.EqualTo(0.8).Within(Eps));
            Assert.That(squash.Easing, Is.EqualTo(Easing.Wobble));
            Assert.That(squash.EasingParam, Is.EqualTo(2));
            Assert.That(Tw(tl, 4, PoseProperty.Roll).Last().EasingParam, Is.EqualTo(4));

            // everything has settled when the landing of tile 4 is over: 430 + 71.43 + 320
            Assert.That(tl.TotalMs, Is.EqualTo(430 + fall + 320).Within(1e-6));
            Assert.That(tl.EndMs, Is.EqualTo(255 + 900).Within(Eps), "the popup outlives the settle");
            Assert.That(tl.Warnings, Is.Empty);
        }

        [Test]
        public void A_piece_cannot_move_again_before_its_previous_move_and_landing_ended()
        {
            var b = Board3();
            var tl = Plan(b, null,
                new Cleared(new[] { 0 }, 0),
                new Moved(3, C(3, 0)),
                new Moved(3, C(0, 3)));
            var moves = Tw(tl, 3, PoseProperty.Position);
            Assert.That(moves.Length, Is.EqualTo(2));
            double first = 250 + 0; // clear 0..250, falls start at 250
            Assert.That(moves[0].StartMs, Is.EqualTo(first).Within(Eps));
            Assert.That(moves[1].StartMs, Is.EqualTo(first + 1000.0 / 14 + 150).Within(1e-6), "busy until landed + 0.6 * clear");
        }

        [Test]
        public void Later_waves_start_after_fire_times_055_plus_the_stagger()
        {
            var b = Board3();
            // the Line at tile 0 clears with wave 0 and fires over its row; wave 1 clears the other two
            var tl = Plan(b, null,
                new Cleared(new[] { 0 }, 0),
                new SpecialFired(0, C(0, 1, 2)),
                new Cleared(new[] { 1, 2 }, 1));
            Assert.That(One(tl, 0, PoseProperty.Sink).StartMs, Is.EqualTo(0));
            double wave1 = 60 + 300 * 0.55; // stagger + 0.55 * special_fire
            Assert.That(One(tl, 1, PoseProperty.Sink).StartMs, Is.EqualTo(wave1).Within(Eps));
            Assert.That(One(tl, 2, PoseProperty.Sink).StartMs, Is.EqualTo(wave1).Within(Eps));
            var wash = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.LineWash);
            Assert.That(wash.StartMs, Is.EqualTo(0));
            Assert.That(wash.DurationMs, Is.EqualTo(300).Within(Eps));
            Assert.That(wash.Axis, Is.EqualTo(0));
            Assert.That(wash.Length, Is.EqualTo(3).Within(Eps), "two cells apart plus one");
            Assert.That(wash.Position, Is.EqualTo(new Float2(0, 1)));
            Assert.That(tl.TotalMs, Is.EqualTo(wave1 + 250).Within(Eps));
        }

        [Test]
        public void Bomb_and_color_specials_show_their_own_washes()
        {
            var b = Board3(new[] { "0b 1 2", "1 0c 2", "0 1 2" });
            var g = BoardGeometry.FromTopology(b.Topology);
            var tl = Plan(b, null,
                new Cleared(new[] { 0, 4 }, 0),
                new SpecialFired(0, C(0, 1, 3, 4)),
                new SpecialFired(4, C(8, 4, 5, 7)));
            var bomb = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.BombWash);
            Assert.That(bomb.Position, Is.EqualTo(g.CellPosition(0)));
            Assert.That(bomb.Radius, Is.EqualTo(g.CellPosition(0).Distance(g.CellPosition(4)) + 0.5).Within(Eps));
            var color = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.ColorWash);
            Assert.That(color.Position, Is.EqualTo(g.CellPosition(4)));
            Assert.That(color.Cells.Count, Is.EqualTo(4));
            Assert.That(color.Cells[0], Is.EqualTo(g.CellPosition(4)), "nearest first: the origin itself");
            var dist = color.Cells.Select(c => c.Distance(color.Position)).ToArray();
            Assert.That(dist, Is.Ordered);
            Assert.That(color.CellProgress(0, 0.5), Is.EqualTo(1.0).Within(Eps));
            Assert.That(color.CellProgress(3, 0.5), Is.EqualTo(0.25).Within(Eps));
        }

        [Test]
        public void Special_creation_pops_and_waits_for_everything_before_it()
        {
            var b = Board3();
            var tl = Plan(b, null,
                new Swapped(3, 4),
                new SpecialCreated(4, new Special.Bomb()));
            var set = tl.Of<SetSpecial>().Single();
            Assert.That(set.TileId, Is.EqualTo(4));
            Assert.That(set.StartMs, Is.EqualTo(180).Within(Eps));
            var pop = One(tl, 4, PoseProperty.Squash);
            Assert.That(pop.StartMs, Is.EqualTo(180).Within(Eps));
            Assert.That(pop.DurationMs, Is.EqualTo(250).Within(Eps));
            Assert.That(pop.To.X, Is.EqualTo(1.3).Within(Eps));
            Assert.That(tl.TotalMs, Is.EqualTo(430).Within(Eps));
        }

        [Test]
        public void Shuffle_squashes_every_piece_and_recolors_at_the_midpoint()
        {
            var b = Board3();
            var tl = Plan(b, null,
                new Shuffled(new[] { (0, 4), (1, 3), (2, 2) }));
            foreach (int tile in new[] { 0, 1, 2 })
            {
                var sq = One(tl, tile, PoseProperty.Squash);
                Assert.That(sq.DurationMs, Is.EqualTo(400).Within(Eps));
                Assert.That(sq.To.X, Is.EqualTo(0.3).Within(Eps));
            }
            var recolors = tl.Of<SetColor>().ToArray();
            Assert.That(recolors.Select(r => (r.TileId, r.Color)), Is.EquivalentTo(new[] { (0, 4), (1, 3), (2, 2) }));
            Assert.That(recolors.All(r => Math.Abs(r.StartMs - 200) < Eps));
            Assert.That(tl.TotalMs, Is.EqualTo(400).Within(Eps));
        }

        [Test]
        public void Each_new_round_resets_the_spawn_stack_but_spawns_within_a_round_never_overlap()
        {
            var b = Board3();
            var tl = Plan(b, null,
                new Cleared(new[] { 0, 3 }, 0),
                new Spawned(10, 0, 0, 1), new Spawned(11, 0, 0, 1), new Spawned(12, 0, 0, 1),
                new Cleared(new[] { 1 }, 0),
                new Spawned(13, 1, 1, 1), new Spawned(14, 0, 0, 1));
            var g = BoardGeometry.FromTopology(b.Topology);
            double Height(int tile, int cell) => tl.Of<SpawnPiece>().Single(s => s.TileId == tile).Position.Y - g.CellPosition(cell).Y;
            Assert.That(Height(10, 0), Is.EqualTo(1.6).Within(Eps));
            Assert.That(Height(11, 0), Is.EqualTo(2.6).Within(Eps));
            Assert.That(Height(12, 0), Is.EqualTo(3.6).Within(Eps));
            Assert.That(Height(13, 1), Is.EqualTo(1.6).Within(Eps));
            Assert.That(Height(14, 0), Is.EqualTo(1.6).Within(Eps), "the stack restarts after the next Cleared");
        }

        [Test]
        public void Animation_speed_divides_durations_and_multiplies_the_fall_speed()
        {
            var b = Board3();
            Event[] ev =
            {
                new Swapped(3, 4), new Cleared(new[] { 0, 1, 2 }, 0), new Moved(4, C(3, 0)), new Spawned(10, 0, 0, 1),
            };
            var normal = Plan(b, TimingParams.Default, ev);
            var fast = Plan(b, TimingParams.Default with { Speed = 2.0 }, ev);
            Assert.That(One(fast, 0, PoseProperty.Sink).DurationMs, Is.EqualTo(125).Within(Eps));
            Assert.That(Tw(fast, 4, PoseProperty.Position).Last().DurationMs, Is.EqualTo(1000.0 / 28).Within(Eps));
            Assert.That(fast.TotalMs, Is.EqualTo(normal.TotalMs / 2).Within(1e-9));
        }

        [Test]
        public void Items_are_sorted_by_start_and_orders_are_unique()
        {
            var b = Board3();
            var tl = Plan(b, null,
                new Swapped(3, 4), new Cleared(new[] { 0, 1, 2 }, 0), new Scored(60, 0),
                new Spawned(10, 0, 0, 1), new Moved(4, C(3, 0)));
            for (int i = 1; i < tl.Items.Count; i++) Assert.That(tl.Items[i].StartMs, Is.GreaterThanOrEqualTo(tl.Items[i - 1].StartMs));
            Assert.That(tl.Items.Select(i => i.Order).Distinct().Count(), Is.EqualTo(tl.Items.Count));
            Assert.That(tl.Items[tl.Items.Count - 1], Is.TypeOf<SyncToFinalBoard>());
        }

        [Test]
        public void Inconsistent_events_are_reported_not_thrown_and_the_sync_marker_is_still_there()
        {
            var b = Board3();
            var tl = Plan(b, null, new Cleared(new[] { 99 }, 0), new Moved(98, C(3, 0)), new SpecialFired(97, C(0, 1)));
            Assert.That(tl.Warnings.Count, Is.GreaterThan(0));
            Assert.That(tl.Items[tl.Items.Count - 1], Is.TypeOf<SyncToFinalBoard>());
        }

        [Test]
        public void Planning_does_not_modify_the_board_and_is_deterministic()
        {
            var b = Board3();
            var before = T.Snapshot(b);
            Event[] ev = { new Swapped(3, 4), new Cleared(new[] { 0, 1, 2 }, 0), new Spawned(10, 0, 0, 1) };
            var a = Plan(b, null, ev);
            var c = Plan(b, null, ev);
            Assert.That(T.Snapshot(b), Is.EqualTo(before));
            Assert.That(a.Items.Select(i => i.ToString()), Is.EqualTo(c.Items.Select(i => i.ToString())));
        }

        [Test]
        public void Select_and_hint_timelines_use_the_pose_properties_without_a_sync_marker()
        {
            var sel = PoseTimelines.Select(5, 0.25, true, Timing with { Speed = 2.0 });
            var tw = sel.Of<Tween>().Single();
            Assert.That(tw.Property, Is.EqualTo(PoseProperty.SelectT));
            Assert.That(tw.DurationMs, Is.EqualTo(45).Within(Eps));
            Assert.That(tw.From.X, Is.EqualTo(0.25));
            Assert.That(tw.To.X, Is.EqualTo(1));
            Assert.That(sel.Items.OfType<SyncToFinalBoard>(), Is.Empty);

            var hint = PoseTimelines.Hint(new[] { 1, 2 }, Timing);
            Assert.That(hint.Of<Tween>().Select(t => t.TileId), Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(hint.TotalMs, Is.EqualTo(1600).Within(Eps));
            var h = hint.Of<Tween>().First();
            Assert.That(h.ValueAt(1600).X, Is.EqualTo(0).Within(1e-9), "the hint pulse ends back at rest");
        }

        static PlaySound[] Sounds(Timeline t) => t.Of<PlaySound>().OrderBy(x => x.StartMs).ToArray();

        [Test]
        public void Swap_and_rejected_swap_start_their_sound_with_the_motion()
        {
            var b = Board3();
            var ok = Sounds(Plan(b, null, new Swapped(3, 4)));
            Assert.That(ok.Select(s => s.Cue), Is.EqualTo(new[] { SoundCue.Swap }));
            Assert.That(ok[0].StartMs, Is.EqualTo(0));
            var bad = Sounds(Plan(b, null, new SwapRejected(3, 4)));
            Assert.That(bad.Select(s => s.Cue), Is.EqualTo(new[] { SoundCue.Reject }));
        }

        [Test]
        public void Each_clear_wave_sounds_and_the_cascade_depth_counts_wave_zero_rounds()
        {
            var b = Board3(new[] { "0 0 0", "1 2 1", "2 1 2" });
            var tl = Plan(b, null,
                new Matched(C(0, 1, 2), new[] { new Qualifier(QualifierKind.Run3, 3) }), new Cleared(new[] { 0, 1, 2 }, 0), new Scored(60, 0),
                new Matched(C(3, 4, 5), new[] { new Qualifier(QualifierKind.Run3, 3) }), new Cleared(new[] { 3, 4, 5 }, 0), new Scored(90, 1));
            var clears = Sounds(tl).Where(s => s.Cue == SoundCue.Clear).ToArray();
            Assert.That(clears.Select(s => s.Depth), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(clears[1].StartMs, Is.GreaterThan(clears[0].StartMs));
        }

        // ---- combo celebration (PRD 5.4.1)

        static readonly Qualifier Run3 = new Qualifier(QualifierKind.Run3, 3);

        [Test]
        public void Celebration_tiers_follow_the_multiplier_thresholds()
        {
            var c = CelebrationParams.Default;
            Assert.That(new[] { 1.0, 1.49, 1.5, 2.5, 3.99, 4.0, 6.0, 12.0 }.Select(c.TierOf), Is.EqualTo(new[] { 0, 0, 1, 2, 2, 3, 4, 4 }));
        }

        [Test]
        public void A_plain_match_has_a_popup_but_no_badge_burst_or_punch()
        {
            var b = Board3(new[] { "0 0 0", "1 2 1", "2 1 2" });
            var tl = Plan(b, null, new Matched(C(0, 1, 2), new[] { Run3 }), new Cleared(new[] { 0, 1, 2 }, 0), new Scored(60, 0));
            Assert.That(tl.Of<ShowFx>().Select(f => f.Kind), Is.EqualTo(new[] { FxKind.Ripple, FxKind.Ripple, FxKind.Ripple, FxKind.ScorePopup }));
            Assert.That(tl.Of<CameraPunch>(), Is.Empty);
        }

        [Test]
        public void A_raised_multiplier_pops_a_badge_and_a_tier_two_combo_adds_burst_punch_and_stinger()
        {
            var b = Board3(new[] { "0 0 0", "1 2 1", "2 1 2" });
            var tl = Plan(b, null, new Matched(C(0, 1, 2), new[] { Run3 }), new Cleared(new[] { 0, 1, 2 }, 0), new Scored(156, 0, 2.6));
            var badge = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.ComboBadge);
            Assert.That(badge.Text, Is.EqualTo("x2.6"));
            Assert.That(badge.Tier, Is.EqualTo(2));
            Assert.That(badge.Size, Is.EqualTo(1.0 + 0.35 * 2).Within(Eps));
            var popup = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.ScorePopup);
            Assert.That(popup.Text, Is.EqualTo("+156"));
            Assert.That(popup.Multiplier, Is.EqualTo(2.6));
            Assert.That(tl.Of<ShowFx>().Single(f => f.Kind == FxKind.Burst).Count, Is.EqualTo(20));
            Assert.That(tl.Of<CameraPunch>().Single().Amount, Is.EqualTo(0.024).Within(Eps));
            Assert.That(Sounds(tl).Any(x => x.Cue == SoundCue.Stinger));
            Assert.That(tl.Of<ScoreApplied>().Single().Multiplier, Is.EqualTo(2.6));
        }

        [Test]
        public void Tier_zero_combo_shows_only_the_badge()
        {
            var b = Board3(new[] { "0 0 0", "1 2 1", "2 1 2" });
            var tl = Plan(b, null, new Matched(C(0, 1, 2), new[] { Run3 }), new Cleared(new[] { 0, 1, 2 }, 0), new Scored(75, 0, 1.25));
            Assert.That(tl.Of<ShowFx>().Count(f => f.Kind == FxKind.ComboBadge), Is.EqualTo(1));
            Assert.That(tl.Of<ShowFx>().Any(f => f.Kind == FxKind.Burst), Is.False);
            Assert.That(tl.Of<CameraPunch>(), Is.Empty);
            Assert.That(Sounds(tl).Any(x => x.Cue == SoundCue.Stinger), Is.False);
        }

        [Test]
        public void Reduce_effects_keeps_the_badge_but_drops_burst_and_punch()
        {
            var b = Board3(new[] { "0 0 0", "1 2 1", "2 1 2" });
            var calm = Timing with { Celebration = CelebrationParams.Default with { ReduceEffects = true } };
            var tl = Plan(b, calm, new Matched(C(0, 1, 2), new[] { Run3 }), new Cleared(new[] { 0, 1, 2 }, 0), new Scored(240, 0, 4.0));
            Assert.That(tl.Of<ShowFx>().Any(f => f.Kind == FxKind.ComboBadge));
            Assert.That(tl.Of<ShowFx>().Any(f => f.Kind == FxKind.Burst), Is.False);
            Assert.That(tl.Of<CameraPunch>(), Is.Empty);
        }

        [Test]
        public void Two_groups_in_a_round_get_their_own_popups_staggered_in_time()
        {
            var b = Board3(new[] { "0 0 0", "1 2 1", "2 2 2" });
            var tl = Plan(b, null,
                new Matched(C(0, 1, 2), new[] { Run3 }), new Matched(C(6, 7, 8), new[] { Run3 }),
                new Cleared(new[] { 0, 1, 2, 6, 7, 8 }, 0),
                new Scored(60, 0, 1.0), new Scored(75, 0, 1.25));
            var popups = tl.Of<ShowFx>().Where(f => f.Kind == FxKind.ScorePopup).OrderBy(f => f.StartMs).ToArray();
            Assert.That(popups.Select(p => p.Text), Is.EqualTo(new[] { "+60", "+75" }));
            Assert.That(popups[1].Position, Is.Not.EqualTo(popups[0].Position), "each group is celebrated over its own cells");
            Assert.That(popups[1].StartMs - popups[0].StartMs, Is.EqualTo(140).Within(Eps));
            var badge = tl.Of<ShowFx>().Single(f => f.Kind == FxKind.ComboBadge);
            Assert.That(badge.Text, Is.EqualTo("x1.25"));
            Assert.That(badge.Position, Is.EqualTo(popups[1].Position));
        }

        [Test]
        public void Sound_times_scale_with_the_animation_speed()
        {
            var b = Board3();
            var fast = Timing with { Speed = 2.0 };
            var tl = Plan(b, fast, new Swapped(3, 4), new Matched(C(3, 4, 5), new[] { new Qualifier(QualifierKind.Run3, 3) }), new Cleared(new[] { 3, 4, 5 }, 0));
            var clear = Sounds(tl).Single(s => s.Cue == SoundCue.Clear);
            Assert.That(clear.StartMs, Is.EqualTo(90).Within(Eps), "after the 180 ms swap at double speed");
        }

        [Test]
        public void Shuffle_special_creation_and_landing_have_sounds()
        {
            var b = Board3(new[] { "0 1 2", "1 0 2", "0 1 2" });
            var shuffle = Sounds(Plan(b, null, new Shuffled(new[] { (0, 3) })));
            Assert.That(shuffle.Select(s => s.Cue), Is.EqualTo(new[] { SoundCue.Shuffle }));
            var created = Sounds(Plan(b, null, new SpecialCreated(4, new Special.Bomb())));
            Assert.That(created.Select(s => s.Cue), Is.EqualTo(new[] { SoundCue.SpecialCreate }));
            var moved = Sounds(Plan(b, null, new Moved(0, C(0, 3))));
            Assert.That(moved.Select(s => s.Cue), Is.EqualTo(new[] { SoundCue.Land }));
            Assert.That(moved[0].StartMs, Is.GreaterThan(0), "the landing sounds when the piece arrives");
        }
    }
}
