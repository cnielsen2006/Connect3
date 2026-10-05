using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Presentation;
using Cascade.Tests.CoreTests;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 9.1, Presentation/Timeline: the sampler the Unity player drives (instants once, tween values, pause/resume/seek).</summary>
    public class TimelineSamplerTests
    {
        const double Eps = 1e-9;

        static (Timeline Tl, BoardGeometry G) Sample()
        {
            var b = T.BoardFrom(new[] { "0 1 2", "1 0 2", "0 1 2" });
            var g = BoardGeometry.FromTopology(b.Topology);
            var ev = new Event[]
            {
                new Swapped(3, 4),
                new Cleared(new[] { 0, 1, 2 }, 0),
                new Scored(60, 0),
                new Spawned(10, 0, 0, 1),
                new Moved(4, new CellId[] { 3, 0 }),
            };
            return (TimelinePlanner.Plan(b, ev, TimingParams.Default, g), g);
        }

        [Test]
        public void Instants_fire_exactly_once_in_time_order_however_the_time_is_stepped()
        {
            var (tl, _) = Sample();
            var instants = tl.Items.OfType<Instant>().ToList();
            foreach (double step in new[] { 1.0, 7.0, 33.3, 1000.0 })
            {
                var s = new TimelineSampler(tl);
                var fired = new List<Instant>();
                for (double t = 0; ; t += step)
                {
                    var f = s.Advance(t);
                    fired.AddRange(f.Fired);
                    if (f.Finished) break;
                }
                Assert.That(fired, Is.EqualTo(instants), "step " + step);
            }
        }

        [Test]
        public void Nothing_fires_early_and_a_tween_at_time_zero_is_sampled_on_the_first_call()
        {
            var b = T.BoardFrom(new[] { "0 1 2", "1 0 2", "0 1 2" });
            var tl = TimelinePlanner.Plan(b, new Event[] { new Shuffled(new[] { (0, 3) }) }, TimingParams.Default, BoardGeometry.FromTopology(b.Topology));
            var f = new TimelineSampler(tl).Advance(0);
            Assert.That(f.Fired.Where(i => !(i is PlaySound)), Is.Empty, "the recolor is at the squash midpoint, not at 0");
            Assert.That(tl.Of<Tween>().Single().StartMs, Is.EqualTo(0));
            Assert.That(f.Poses.Single().Scalar, Is.EqualTo(1.0).Within(Eps));
        }

        [Test]
        public void Tween_values_follow_the_easing_and_hold_their_final_value()
        {
            var (tl, g) = Sample();
            var s = new TimelineSampler(tl);
            var f = s.Advance(90); // half of the 180 ms swap
            var pos = f.Poses.Single(p => p.TileId == 3 && p.Property == PoseProperty.Position);
            var mid = Float2.Lerp(g.CellPosition(3), g.CellPosition(4), 0.5);
            Assert.That(pos.Value.Near(mid, 1e-9), Is.True, "smooth-step is 0.5 at the middle");
            Assert.That(f.Poses.Single(p => p.TileId == 3 && p.Property == PoseProperty.Roll).Scalar, Is.EqualTo(7).Within(Eps), "the rock peaks mid-swap");
            Assert.That(f.Poses.Any(p => p.TileId == 10), Is.False, "nothing is driven for a tile that has not started");

            f = s.Advance(200);
            Assert.That(f.Poses.Single(p => p.TileId == 3 && p.Property == PoseProperty.Position).Value.Near(g.CellPosition(4), 1e-9), Is.True, "finished tween holds");
            Assert.That(f.Poses.Single(p => p.TileId == 3 && p.Property == PoseProperty.Roll).Scalar, Is.EqualTo(0).Within(Eps));
        }

        [Test]
        public void The_latest_started_tween_wins_for_a_property_and_removed_tiles_are_not_driven()
        {
            var (tl, g) = Sample();
            var s = new TimelineSampler(tl);
            var f = s.Advance(tl.EndMs);
            var four = f.Poses.Where(p => p.TileId == 4 && p.Property == PoseProperty.Position).ToList();
            Assert.That(four.Count, Is.EqualTo(1));
            Assert.That(four[0].Value.Near(g.CellPosition(0), 1e-9), Is.True, "the fall replaced the swap slide");
            Assert.That(f.Poses.Any(p => p.TileId == 0 || p.TileId == 1 || p.TileId == 2), Is.False, "cleared and removed");
        }

        [Test]
        public void Fx_start_live_and_end_are_reported_once_each()
        {
            var (tl, _) = Sample();
            var s = new TimelineSampler(tl);
            var started = new List<int>();
            var ended = new List<int>();
            bool aliveSeen = false;
            for (double t = 0; ; t += 10)
            {
                var f = s.Advance(t);
                started.AddRange(f.FxStarted.Select(x => x.Id));
                ended.AddRange(f.FxEnded.Select(x => x.Id));
                foreach (var live in f.Fx)
                {
                    aliveSeen = true;
                    Assert.That(live.U, Is.InRange(0.0, 1.0));
                    Assert.That(t, Is.GreaterThanOrEqualTo(live.Fx.StartMs));
                    Assert.That(t, Is.LessThan(live.Fx.EndMs));
                }
                if (f.Finished) break;
            }
            Assert.That(aliveSeen);
            var all = tl.Of<ShowFx>().Select(x => x.Id).OrderBy(x => x).ToList();
            Assert.That(started.OrderBy(x => x).ToList(), Is.EqualTo(all));
            Assert.That(ended.OrderBy(x => x).ToList(), Is.EqualTo(all));
        }

        [Test]
        public void A_big_jump_skips_effects_that_began_and_ended_inside_it()
        {
            var (tl, _) = Sample();
            var s = new TimelineSampler(tl);
            var f = s.Advance(tl.EndMs + 100);
            Assert.That(f.FxStarted, Is.Empty);
            Assert.That(f.FxEnded, Is.Empty);
            Assert.That(f.Fired.OfType<SyncToFinalBoard>().Count(), Is.EqualTo(1));
            Assert.That(f.Settled && f.Finished);
        }

        [Test]
        public void Settled_comes_before_finished_when_a_popup_overhangs()
        {
            var (tl, _) = Sample();
            var s = new TimelineSampler(tl);
            var f = s.Advance(tl.TotalMs);
            Assert.That(f.Settled, Is.True);
            Assert.That(f.Fired.OfType<SyncToFinalBoard>().Count(), Is.EqualTo(1));
            Assert.That(tl.EndMs, Is.GreaterThan(tl.TotalMs));
            Assert.That(f.Finished, Is.False);
            Assert.That(f.Fx.Any(x => x.Fx.Kind == FxKind.ScorePopup), Is.True);
        }

        [Test]
        public void Pause_resume_is_just_not_advancing_and_a_backward_time_restarts()
        {
            var (tl, _) = Sample();
            var s = new TimelineSampler(tl);
            var a = s.Advance(100);
            var same = s.Advance(100);
            Assert.That(same.Fired, Is.Empty, "no time passed: nothing fires twice");
            Assert.That(same.Poses.Select(p => p.Value), Is.EqualTo(a.Poses.Select(p => p.Value)));
            s.Advance(500);
            var again = s.Advance(100);
            Assert.That(again.Fired.Count, Is.EqualTo(a.Fired.Count), "rewinding restarts the sampler");
            Assert.That(again.Poses.Select(p => p.Value), Is.EqualTo(a.Poses.Select(p => p.Value)));
        }

        [Test]
        public void Tween_value_at_is_clamped_outside_its_time_range()
        {
            var tw = new Tween(1, PoseProperty.Sink, new Float2(0, 0), new Float2(1, 0), 100, 200, Easing.Linear);
            Assert.That(tw.ValueAt(0).X, Is.EqualTo(0));
            Assert.That(tw.ValueAt(200).X, Is.EqualTo(0.5).Within(Eps));
            Assert.That(tw.ValueAt(9999).X, Is.EqualTo(1));
            var instant = new Tween(1, PoseProperty.Sink, new Float2(0, 0), new Float2(1, 0), 100, 0, Easing.Linear);
            Assert.That(instant.ValueAt(99).X, Is.EqualTo(0));
            Assert.That(instant.ValueAt(100).X, Is.EqualTo(1));
        }

        [Test]
        public void Path_tween_walks_the_polyline_by_length()
        {
            var pts = new[] { new Float2(0, 0), new Float2(0, 1), new Float2(0, 3) };
            var tw = new Tween(1, PoseProperty.Position, pts[0], pts[2], 0, 1000, Easing.Linear, 0, pts);
            Assert.That(tw.ValueAt(500).Y, Is.EqualTo(1.5).Within(Eps));
            Assert.That(tw.ValueAt(1000).Y, Is.EqualTo(3).Within(Eps));
            Assert.That(tw.ValueAt(100).Y, Is.EqualTo(0.3).Within(Eps));
        }

        [Test]
        public void Easings_have_the_documented_shapes()
        {
            Assert.That(Easings.Eval(Easing.SmoothStep, 0.5), Is.EqualTo(0.5).Within(Eps));
            Assert.That(Easings.Eval(Easing.QuadIn, 0.5), Is.EqualTo(0.25).Within(Eps));
            Assert.That(Easings.Eval(Easing.FallAccel, 1), Is.EqualTo(1).Within(Eps));
            Assert.That(Easings.Eval(Easing.FallAccel, 0.5), Is.EqualTo(0.175 + 0.1625).Within(Eps));
            foreach (var e in new[] { Easing.Bump, Easing.BumpPow15, Easing.Wobble, Easing.Pulse })
            {
                Assert.That(Easings.Eval(e, 0, 2), Is.EqualTo(0).Within(Eps), e + " at 0");
                Assert.That(Easings.Eval(e, 1, 2), Is.EqualTo(0).Within(Eps), e + " returns");
            }
            Assert.That(Easings.Eval(Easing.Bump, 0.5), Is.EqualTo(1).Within(Eps));
        }
    }
}
