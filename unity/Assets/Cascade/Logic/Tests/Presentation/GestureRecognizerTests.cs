using System.Collections.Generic;
using System.Linq;
using Cascade.Presentation;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>
    /// TRD 9.1, Presentation/Gestures: scripted touch sequences. Unless a test says otherwise the screen is
    /// 254 dpi, so 1 mm = 10 px: tap slop 2 mm = 20 px, pinch threshold 4 mm = 40 px, double tap distance 8 mm = 80 px,
    /// double tap time 300 ms.
    /// </summary>
    public class GestureRecognizerTests
    {
        const double Eps = 1e-9;

        static GestureRecognizer Make(GestureParams? p = null, double dpi = 254) => new GestureRecognizer(p ?? GestureParams.Default, dpi);

        static List<GestureEvent> All(params IReadOnlyList<GestureEvent>[] steps) => steps.SelectMany(s => s).ToList();

        // ---- tap ------------------------------------------------------------------------------------------

        [Test]
        public void Tap_is_a_press_and_release_within_the_slop()
        {
            var g = Make();
            Assert.That(g.PointerDown(1, 100, 100, 0), Is.Empty);
            Assert.That(g.PointerMove(1, 110, 105, 20), Is.Empty, "11 px is inside the 20 px slop");
            Assert.That(g.PointerUp(1, 110, 105, 60), Is.EqualTo(new GestureEvent[] { new GestureEvent.Tap(110, 105) }));
        }

        [Test]
        public void Slop_comes_from_the_params_in_millimetres_and_the_dpi()
        {
            // 30 px of movement: a drag at 2 mm / 254 dpi (20 px) ...
            var g = Make();
            g.PointerDown(1, 0, 0, 0);
            Assert.That(g.PointerMove(1, 30, 0, 10).OfType<GestureEvent.DragStart>().Count(), Is.EqualTo(1));

            // ... a tap at 5 mm / 254 dpi (50 px)
            g = Make(GestureParams.Default with { TapSlopMm = 5 });
            g.PointerDown(1, 0, 0, 0);
            Assert.That(g.PointerMove(1, 30, 0, 10), Is.Empty);
            Assert.That(g.PointerUp(1, 30, 0, 20).Single(), Is.TypeOf<GestureEvent.Tap>());

            // the same 30 px at 127 dpi: 2 mm = 10 px, a drag again; and 15 px would be a drag, 8 px a tap
            g = Make(dpi: 127);
            g.PointerDown(1, 0, 0, 0);
            Assert.That(g.PointerMove(1, 8, 0, 5), Is.Empty);
            Assert.That(g.PointerMove(1, 15, 0, 10).OfType<GestureEvent.DragStart>().Count(), Is.EqualTo(1));
        }

        // ---- double tap -----------------------------------------------------------------------------------

        [Test]
        public void Two_quick_taps_close_together_are_a_double_tap()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            Assert.That(g.PointerUp(1, 100, 100, 50), Is.EqualTo(new GestureEvent[] { new GestureEvent.Tap(100, 100) }));
            g.PointerDown(1, 120, 110, 200);   // 150 ms after the release, 22 px away
            Assert.That(g.PointerUp(1, 120, 110, 240), Is.EqualTo(new GestureEvent[] { new GestureEvent.Tap(120, 110), new GestureEvent.DoubleTap(120, 110) }));
        }

        [Test]
        public void Taps_too_slow_or_too_far_apart_are_two_single_taps()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0); g.PointerUp(1, 100, 100, 50);
            g.PointerDown(1, 100, 100, 351);   // 301 ms after the release
            Assert.That(g.PointerUp(1, 100, 100, 380).Select(e => e.GetType()), Is.EqualTo(new[] { typeof(GestureEvent.Tap) }));

            g = Make();
            g.PointerDown(1, 100, 100, 0); g.PointerUp(1, 100, 100, 50);
            g.PointerDown(1, 300, 100, 100);   // 200 px = 20 mm away
            Assert.That(g.PointerUp(1, 300, 100, 120).Select(e => e.GetType()), Is.EqualTo(new[] { typeof(GestureEvent.Tap) }));
        }

        [Test]
        public void The_double_tap_time_comes_from_the_params_and_a_third_tap_starts_a_new_chain()
        {
            var g = Make(GestureParams.Default with { DoubleTapMs = 500 });
            g.PointerDown(1, 0, 0, 0); g.PointerUp(1, 0, 0, 50);
            g.PointerDown(1, 0, 0, 500); // 450 ms later: inside 500
            Assert.That(g.PointerUp(1, 0, 0, 520).OfType<GestureEvent.DoubleTap>().Count(), Is.EqualTo(1));
            g.PointerDown(1, 0, 0, 600);
            Assert.That(g.PointerUp(1, 0, 0, 620).OfType<GestureEvent.DoubleTap>().Count(), Is.EqualTo(0), "the third tap is not another double tap");
            g.PointerDown(1, 0, 0, 700);
            Assert.That(g.PointerUp(1, 0, 0, 720).OfType<GestureEvent.DoubleTap>().Count(), Is.EqualTo(1), "but the fourth pairs with it");
        }

        [Test]
        public void A_drag_between_two_taps_breaks_the_double_tap()
        {
            var g = Make();
            g.PointerDown(1, 0, 0, 0); g.PointerUp(1, 0, 0, 30);
            g.PointerDown(1, 0, 0, 60); g.PointerMove(1, 100, 0, 80); g.PointerUp(1, 100, 0, 90);
            g.PointerDown(1, 100, 0, 120);
            Assert.That(g.PointerUp(1, 100, 0, 130).OfType<GestureEvent.DoubleTap>(), Is.Empty);
        }

        // ---- drag -------------------------------------------------------------------------------------------

        [Test]
        public void Drag_start_move_end_after_leaving_the_slop_and_no_tap()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            var a = g.PointerMove(1, 140, 100, 20);
            Assert.That(a, Is.EqualTo(new GestureEvent[] { new GestureEvent.DragStart(100, 100), new GestureEvent.DragMove(140, 100) }));
            Assert.That(g.PointerMove(1, 160, 110, 40), Is.EqualTo(new GestureEvent[] { new GestureEvent.DragMove(160, 110) }));
            Assert.That(g.PointerUp(1, 160, 110, 60), Is.EqualTo(new GestureEvent[] { new GestureEvent.DragEnd(160, 110) }));
        }

        [Test]
        public void Once_dragging_the_finger_may_come_back_without_becoming_a_tap()
        {
            var g = Make();
            g.PointerDown(1, 0, 0, 0);
            g.PointerMove(1, 100, 0, 10);
            g.PointerMove(1, 0, 0, 20);
            var up = g.PointerUp(1, 0, 0, 30);
            Assert.That(up.Single(), Is.TypeOf<GestureEvent.DragEnd>());
        }

        // ---- second finger ------------------------------------------------------------------------------------

        [Test]
        public void A_second_finger_cancels_a_drag_in_progress_and_the_first_finger_then_does_nothing()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            g.PointerMove(1, 150, 100, 10);
            var cancel = g.PointerDown(2, 300, 100, 20);
            Assert.That(cancel, Is.EqualTo(new GestureEvent[] { new GestureEvent.Cancel() }));
            Assert.That(g.PointerUp(1, 150, 100, 30), Is.Empty, "no DragEnd, no Tap");
            Assert.That(g.PointerMove(2, 320, 100, 40), Is.Empty);
            Assert.That(g.PointerUp(2, 320, 100, 50), Is.Empty);
            // everyone is up: a new touch is a normal tap
            g.PointerDown(3, 10, 10, 1000);
            Assert.That(g.PointerUp(3, 10, 10, 1010).Single(), Is.TypeOf<GestureEvent.Tap>());
        }

        [Test]
        public void A_second_finger_during_a_pending_tap_cancels_it()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            Assert.That(g.PointerDown(2, 200, 100, 10), Is.EqualTo(new GestureEvent[] { new GestureEvent.Cancel() }));
            Assert.That(g.PointerUp(2, 200, 100, 20), Is.Empty);
            Assert.That(g.PointerUp(1, 100, 100, 30), Is.Empty, "a two-finger touch never ends in a tap");
        }

        [Test]
        public void A_third_finger_is_ignored()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            g.PointerDown(2, 300, 100, 5);
            Assert.That(g.PointerDown(3, 500, 500, 6), Is.Empty);
            Assert.That(g.PointerMove(3, 600, 500, 7), Is.Empty);
            Assert.That(g.PointerUp(3, 600, 500, 8), Is.Empty);
            // the first two still pinch
            Assert.That(g.PointerMove(2, 400, 100, 20).OfType<GestureEvent.PinchZoom>().Count(), Is.EqualTo(1));
        }

        // ---- pinch --------------------------------------------------------------------------------------------

        [Test]
        public void Pinch_zooms_by_the_ratio_of_the_spread_around_the_midpoint_after_the_threshold()
        {
            var g = Make();
            g.PointerDown(1, 100, 300, 0);
            g.PointerDown(2, 300, 300, 5);          // spread 200, midpoint (200, 300)
            var small = g.PointerMove(2, 320, 300, 10); // spread 220: below 40 px
            Assert.That(small.OfType<GestureEvent.PinchZoom>(), Is.Empty);
            var first = g.PointerMove(2, 360, 300, 20);  // spread 260: crossed the threshold, catches up the whole 1.3
            var z = first.OfType<GestureEvent.PinchZoom>().Single();
            Assert.That(z.Factor, Is.EqualTo(1.3).Within(Eps));
            Assert.That(z.CenterX, Is.EqualTo(230).Within(Eps));
            Assert.That(z.CenterY, Is.EqualTo(300).Within(Eps));
            var next = g.PointerMove(2, 386, 300, 30);  // spread 286: factor 1.1 from the last
            Assert.That(next.OfType<GestureEvent.PinchZoom>().Single().Factor, Is.EqualTo(286.0 / 260.0).Within(Eps));
            var back = g.PointerMove(2, 286, 300, 40);  // spread 186
            Assert.That(back.OfType<GestureEvent.PinchZoom>().Single().Factor, Is.LessThan(1.0));
        }

        [Test]
        public void The_pinch_threshold_comes_from_the_params()
        {
            var g = Make(GestureParams.Default with { PinchMinMm = 10 }); // 100 px
            g.PointerDown(1, 100, 300, 0);
            g.PointerDown(2, 300, 300, 5);
            Assert.That(g.PointerMove(2, 380, 300, 10).OfType<GestureEvent.PinchZoom>(), Is.Empty, "80 px change < 100 px");
            Assert.That(g.PointerMove(2, 410, 300, 20).OfType<GestureEvent.PinchZoom>().Count(), Is.EqualTo(1));
        }

        // ---- pan ----------------------------------------------------------------------------------------------

        [Test]
        public void Two_fingers_moving_together_pan_after_the_slop_without_zooming()
        {
            var g = Make();
            g.PointerDown(1, 100, 300, 0);
            g.PointerDown(2, 300, 300, 5);
            Assert.That(g.PointerMove(1, 100, 305, 8), Is.Empty, "2.5 px of midpoint movement is inside the 20 px slop");
            var a = g.PointerMove(1, 100, 340, 10);   // midpoint now 20 px down: panning starts and catches up
            var b = g.PointerMove(2, 300, 340, 20);   // and 20 px more
            Assert.That(All(a, b).OfType<GestureEvent.PinchZoom>(), Is.Empty);
            var pans = All(a, b).OfType<GestureEvent.Pan>().ToList();
            Assert.That(pans.Count, Is.EqualTo(2));
            Assert.That(pans.Sum(p => p.Dx), Is.EqualTo(0).Within(Eps));
            Assert.That(pans.Sum(p => p.Dy), Is.EqualTo(40).Within(Eps));
        }

        [Test]
        public void The_first_pan_catches_up_with_the_distance_travelled_since_the_two_fingers_landed()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            g.PointerDown(2, 300, 100, 5);
            var o = g.PointerMove(1, 100, 160, 10);    // finger 1 down 60 px: midpoint down 30 px (> 20 px slop)
            var pan = o.OfType<GestureEvent.Pan>().Single();
            Assert.That(pan.Dx, Is.EqualTo(0).Within(Eps));
            Assert.That(pan.Dy, Is.EqualTo(30).Within(Eps));
        }

        [Test]
        public void Lifting_one_finger_ends_the_gesture_and_the_other_does_nothing_until_everyone_is_up()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            g.PointerDown(2, 300, 100, 5);
            Assert.That(g.PointerUp(2, 300, 100, 20), Is.Empty);
            Assert.That(g.PointerMove(1, 200, 100, 30), Is.Empty);
            Assert.That(g.PointerUp(1, 200, 100, 40), Is.Empty);
            g.PointerDown(1, 0, 0, 500);
            Assert.That(g.PointerUp(1, 0, 0, 510).Single(), Is.TypeOf<GestureEvent.Tap>());
        }

        // ---- cancel -------------------------------------------------------------------------------------------

        [Test]
        public void System_cancel_emits_cancel_once_and_swallows_the_rest_of_the_touch()
        {
            var g = Make();
            g.PointerDown(1, 100, 100, 0);
            g.PointerMove(1, 200, 100, 10);
            Assert.That(g.PointerCancel(1), Is.EqualTo(new GestureEvent[] { new GestureEvent.Cancel() }));
            Assert.That(g.PointerUp(1, 200, 100, 20), Is.Empty);
            Assert.That(g.PointerCancel(9), Is.Empty);
        }

        [Test]
        public void CancelAll_resets_everything_including_a_pending_double_tap()
        {
            var g = Make();
            g.PointerDown(1, 0, 0, 0); g.PointerUp(1, 0, 0, 10);
            g.CancelAll();
            g.PointerDown(1, 0, 0, 20);
            Assert.That(g.PointerUp(1, 0, 0, 30).OfType<GestureEvent.DoubleTap>(), Is.Empty);
            g.PointerDown(1, 0, 0, 40);
            Assert.That(g.CancelAll(), Is.EqualTo(new GestureEvent[] { new GestureEvent.Cancel() }));
        }

        [Test]
        public void Unknown_pointer_ids_are_ignored()
        {
            var g = Make();
            Assert.That(g.PointerMove(7, 0, 0, 0), Is.Empty);
            Assert.That(g.PointerUp(7, 0, 0, 0), Is.Empty);
        }
    }
}
