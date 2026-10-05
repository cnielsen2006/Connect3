using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Presentation;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>
    /// TRD 9.1, Presentation/InputStateMachine: the PRD 2.2 table row by row. 3 x 3 board, cell = row * 3 + column,
    /// every cell holds a tile; the cell grid:  0 1 2 / 3 4 5 / 6 7 8.
    /// </summary>
    public class InputStateMachineTests
    {
        static readonly Topology Topo = Topology.Grid(3, 3);

        static InputStateMachine Make(double inner = 0.6, double dbl = 300, bool hover = true, HashSet<int>? empty = null)
            => new InputStateMachine(c => Topo.Neighbors(c), c => empty != null && empty.Contains(c.Value) ? (int?)null : c.Value, new InputParams(inner, dbl), hover);

        static CellId? Cell(int? c) => c == null ? (CellId?)null : new CellId(c.Value);

        static List<InputIntent> Down(InputStateMachine m, int? cell, double fx = 0, double fy = 0, double t = 0)
            => m.PointerDown(Cell(cell), fx, fy, t).ToList();
        static List<InputIntent> Move(InputStateMachine m, int? cell, double fx = 0, double fy = 0)
            => m.PointerMove(Cell(cell), fx, fy).ToList();
        static List<InputIntent> Up(InputStateMachine m, int? cell, double fx = 0, double fy = 0)
            => m.PointerUp(Cell(cell), fx, fy).ToList();

        static IEnumerable<InputIntent> Real(IEnumerable<InputIntent> i) => i.Where(x => !(x is InputIntent.Hover));

        static void Expect(IEnumerable<InputIntent> actual, params InputIntent[] expected)
            => Assert.That(Real(actual).ToList(), Is.EqualTo(expected.ToList()));

        // ---- row 1: press on a piece ----------------------------------------------------------------------

        [Test]
        public void Row1_press_on_a_piece_selects_it_immediately()
        {
            var m = Make();
            Expect(Down(m, 4), new InputIntent.Select(4));
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        [Test]
        public void Row1_press_on_a_piece_with_a_selection_elsewhere_moves_the_selection_at_once()
        {
            var m = Make();
            Down(m, 0); Up(m, 0);
            Expect(Down(m, 8), new InputIntent.Deselect(0), new InputIntent.Select(8));
            Assert.That(m.Selected, Is.EqualTo((CellId?)8));
        }

        [Test]
        public void Row1_press_on_a_neighbor_of_the_selection_keeps_the_selection_until_release()
        {
            var m = Make();
            Down(m, 4); Up(m, 4);
            Expect(Down(m, 5));
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
            Assert.That(m.PressOrigin, Is.EqualTo((CellId?)5));
        }

        // ---- row 2: drag while holding -------------------------------------------------------------------

        static InputStateMachine MakeWithWorld()
            => new InputStateMachine(c => Topo.Neighbors(c), c => c.Value, new InputParams(0.6, 300), true,
                c => new Float2(c.Value % 3 - 1, -(c.Value / 3 - 1)));

        [Test]
        public void Row2_dragging_far_past_the_neighbor_still_targets_it_by_direction()
        {
            var m = MakeWithWorld();
            m.PointerDown(Cell(4), 0, 0, 0, new Float2(0, 0));
            // well beyond the neighbor (and off the board): the pick is null, the vector still points right
            Expect(m.PointerMove(null, 0, 0, new Float2(3.5, 0.3)).ToList(), new InputIntent.ShowArrow(4, 5));
            Expect(m.PointerUp(null, 0, 0).ToList(), new InputIntent.HideArrow(), new InputIntent.Deselect(4), new InputIntent.Swap(4, 5));
        }

        [Test]
        public void Row2_a_diagonal_drag_or_one_that_has_not_reached_the_neighbor_shows_no_arrow()
        {
            var m = MakeWithWorld();
            m.PointerDown(Cell(4), 0, 0, 0, new Float2(0, 0));
            Assert.That(m.PointerMove(Cell(4), 0, 0, new Float2(1.5, 1.4)).ToList(), Is.Empty);
            Assert.That(m.PointerMove(Cell(4), 0, 0, new Float2(0.3, 0)).ToList(), Is.Empty);
        }

        [Test]
        public void Row2_dragging_inside_the_pressed_cell_or_the_edge_of_a_neighbor_changes_nothing()
        {
            var m = Make();
            Down(m, 4);
            Assert.That(Move(m, 4, 0.2, 0.1), Is.Empty);
            Assert.That(Move(m, 5, -0.4, 0.0), Is.Empty, "barely inside the neighbor's edge");
            Assert.That(m.ArrowTarget, Is.Null);
            Assert.That(Move(m, 8, 0, 0), Is.Empty, "a cell that is not a neighbor");
        }

        // ---- row 3: pointer enters a neighbor's inner zone -----------------------------------------------

        [Test]
        public void Row3_entering_the_inner_zone_of_a_neighbor_shows_the_arrow_and_leaving_hides_it()
        {
            var m = Make();
            Down(m, 4);
            Expect(Move(m, 5, 0.0, 0.0), new InputIntent.ShowArrow(4, 5));
            Assert.That(m.ArrowTarget, Is.EqualTo((CellId?)5));
            Assert.That(Move(m, 5, 0.1, 0.1), Is.Empty, "still inside: no repeat");
            Expect(Move(m, 5, -0.45, 0.0), new InputIntent.HideArrow());
            Assert.That(m.ArrowTarget, Is.Null);
        }

        [Test]
        public void Row3_the_arrow_moves_from_one_neighbor_to_another()
        {
            var m = Make();
            Down(m, 4);
            Expect(Move(m, 5), new InputIntent.ShowArrow(4, 5));
            Expect(Move(m, 7), new InputIntent.ShowArrow(4, 7));
            Expect(Move(m, 4), new InputIntent.HideArrow());
        }

        [Test]
        public void Row3_the_inner_zone_is_the_central_fraction_of_the_cell_and_a_touch_value_is_smaller()
        {
            var mouse = Make(0.6);
            Down(mouse, 4);
            Expect(Move(mouse, 5, 0.29, -0.29), new InputIntent.ShowArrow(4, 5));
            var touch = Make(0.5);
            Down(touch, 4);
            Assert.That(Move(touch, 5, 0.29, -0.29), Is.Empty, "0.29 is outside the central 50%");
            Expect(Move(touch, 5, 0.24, 0.0), new InputIntent.ShowArrow(4, 5));
        }

        // ---- row 4: release with the arrow ---------------------------------------------------------------

        [Test]
        public void Row4_release_with_the_arrow_swaps_and_deselects()
        {
            var m = Make();
            Down(m, 4);
            Move(m, 5);
            Expect(Up(m, 5), new InputIntent.HideArrow(), new InputIntent.Deselect(4), new InputIntent.Swap(4, 5));
            Assert.That(m.Selected, Is.Null);
            Assert.That(m.IsPressed, Is.False);
        }

        [Test]
        public void Row4_release_with_the_arrow_swaps_even_when_the_pointer_has_left_the_zone_cell_on_release()
        {
            var m = Make();
            Down(m, 4);
            Move(m, 5);
            // the arrow was showing on the last move; the release itself carries a slightly different position
            Expect(Up(m, 5, 0.45, 0), new InputIntent.HideArrow(), new InputIntent.Deselect(4), new InputIntent.Swap(4, 5));
        }

        // ---- row 5: release without the arrow ------------------------------------------------------------

        [Test]
        public void Row5_release_without_the_arrow_is_a_click_on_the_original_piece_which_stays_selected()
        {
            var m = Make();
            Down(m, 4);
            Move(m, 5, -0.45, 0);        // barely inside the neighbor's edge
            Expect(Up(m, 5, -0.45, 0));  // no swap, no deselect
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        [Test]
        public void Row5_release_back_on_the_origin_after_a_drag_keeps_it_selected()
        {
            var m = Make();
            Down(m, 4);
            Move(m, 5);
            Move(m, 4);
            Expect(Up(m, 4));
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        // ---- row 6: click a neighbor while a piece is selected -------------------------------------------

        [Test]
        public void Row6_click_on_a_neighbor_of_the_selection_swaps()
        {
            var m = Make();
            Down(m, 4); Up(m, 4);
            Down(m, 5);
            Expect(Up(m, 5), new InputIntent.Deselect(4), new InputIntent.Swap(4, 5));
            Assert.That(m.Selected, Is.Null);
        }

        [Test]
        public void Row6_press_on_a_neighbor_then_releasing_somewhere_else_does_not_swap()
        {
            var m = Make();
            Down(m, 4); Up(m, 4);
            Down(m, 5);
            Expect(Up(m, 8));
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        // ---- row 7: click any other piece ----------------------------------------------------------------

        [Test]
        public void Row7_click_on_any_other_piece_moves_the_selection()
        {
            var m = Make();
            Down(m, 0); Up(m, 0);
            Down(m, 8);
            Expect(Up(m, 8));
            Assert.That(m.Selected, Is.EqualTo((CellId?)8));
        }

        // ---- row 8: click the selected piece again --------------------------------------------------------

        [Test]
        public void Row8_click_on_the_selected_piece_deselects()
        {
            var m = Make();
            Down(m, 4); Up(m, 4);
            Expect(Down(m, 4));
            Expect(Up(m, 4), new InputIntent.Deselect(4));
            Assert.That(m.Selected, Is.Null);
        }

        [Test]
        public void Row8_press_the_selected_piece_and_release_elsewhere_keeps_it_selected()
        {
            var m = Make();
            Down(m, 4); Up(m, 4);
            Down(m, 4);
            Expect(Up(m, 8));
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        [Test]
        public void Row8_dragging_from_the_selected_piece_into_a_neighbor_swaps_it()
        {
            var m = Make();
            Down(m, 4); Up(m, 4);
            Down(m, 4);
            Expect(Move(m, 1), new InputIntent.ShowArrow(4, 1));
            Expect(Up(m, 1), new InputIntent.HideArrow(), new InputIntent.Deselect(4), new InputIntent.Swap(4, 1));
        }

        // ---- touch: second finger -------------------------------------------------------------------------

        [Test]
        public void Touch_second_finger_cancels_the_drag_without_a_swap_and_the_piece_stays_selected()
        {
            var m = Make(0.5, hover: false);
            Down(m, 4);
            Expect(Move(m, 5), new InputIntent.ShowArrow(4, 5));
            Expect(m.Handle(new InputEvent.SecondFingerDown()), new InputIntent.HideArrow());
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
            Assert.That(m.IsPressed, Is.False);
            Expect(Up(m, 5));  // the first finger lifting afterwards does nothing
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        [Test]
        public void Touch_second_finger_during_a_click_on_a_neighbor_does_not_swap()
        {
            var m = Make(0.5, hover: false);
            Down(m, 4); Up(m, 4);
            Down(m, 5);
            Expect(m.Handle(new InputEvent.SecondFingerDown()));
            Expect(Up(m, 5));
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        [Test]
        public void Cancel_event_behaves_like_a_second_finger()
        {
            var m = Make();
            Down(m, 4);
            Move(m, 5);
            Expect(m.Handle(new InputEvent.Cancel()), new InputIntent.HideArrow());
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
            Assert.That(m.IsPressed, Is.False);
        }

        // ---- double click on empty space -------------------------------------------------------------------

        [Test]
        public void Double_click_on_empty_space_resets_the_view()
        {
            var m = Make(dbl: 300);
            Assert.That(Down(m, null, t: 1000), Is.Empty);
            Up(m, null);
            Expect(Down(m, null, t: 1250), new InputIntent.ResetView());
        }

        [Test]
        public void Two_empty_clicks_further_apart_than_the_double_click_time_do_not_reset()
        {
            var m = Make(dbl: 300);
            Down(m, null, t: 1000); Up(m, null);
            Assert.That(Down(m, null, t: 1400), Is.Empty);
            Up(m, null);
            Expect(Down(m, null, t: 1500), new InputIntent.ResetView());
        }

        [Test]
        public void A_click_on_a_piece_between_two_empty_clicks_breaks_the_double_click()
        {
            var m = Make(dbl: 300);
            Down(m, null, t: 0); Up(m, null);
            Down(m, 4, t: 100); Up(m, 4);
            Assert.That(Down(m, null, t: 200), Is.Empty);
        }

        [Test]
        public void An_empty_press_does_not_change_the_selection_and_a_hole_counts_as_empty()
        {
            var m = Make(empty: new HashSet<int> { 7 });
            Down(m, 4); Up(m, 4);
            Assert.That(Down(m, null, t: 0), Is.Empty);
            Assert.That(Down(m, 7, t: 5000), Is.Empty, "a cell without a tile");
            Assert.That(m.Selected, Is.EqualTo((CellId?)4));
        }

        // ---- hover ---------------------------------------------------------------------------------------

        [Test]
        public void Hover_follows_the_pointer_when_no_button_is_down_and_stops_during_a_press()
        {
            var m = Make();
            Assert.That(Move(m, 4), Is.EqualTo(new List<InputIntent> { new InputIntent.Hover((CellId?)4) }));
            Assert.That(Move(m, 4), Is.Empty);
            Assert.That(Move(m, 5), Is.EqualTo(new List<InputIntent> { new InputIntent.Hover((CellId?)5) }));
            Assert.That(Move(m, null), Is.EqualTo(new List<InputIntent> { new InputIntent.Hover(null) }));
            Move(m, 4);
            var down = Down(m, 4);
            Assert.That(down.OfType<InputIntent.Hover>().Single().Cell, Is.Null, "no hover while pressing");
            Assert.That(m.Hovered, Is.Null);
        }

        [Test]
        public void Touch_machines_never_report_hover()
        {
            var m = Make(hover: false);
            Assert.That(Move(m, 4), Is.Empty);
            Down(m, 4);
            Assert.That(Up(m, 4).OfType<InputIntent.Hover>(), Is.Empty);
            Assert.That(m.Hovered, Is.Null);
        }

        // ---- plumbing ------------------------------------------------------------------------------------

        [Test]
        public void Disabling_drops_the_press_and_ignores_events()
        {
            var m = Make();
            Down(m, 4);
            Move(m, 5);
            m.Enabled = false;
            Assert.That(m.IsPressed, Is.False);
            Assert.That(m.ArrowTarget, Is.Null);
            Assert.That(Down(m, 0), Is.Empty);
            Assert.That(Up(m, 0), Is.Empty);
            m.Enabled = true;
            Expect(Down(m, 0), new InputIntent.Deselect(4), new InputIntent.Select(0));
        }

        [Test]
        public void Reset_and_Deselect_clear_the_state_from_outside()
        {
            var m = Make();
            Down(m, 4); Move(m, 5);
            Expect(m.Reset(), new InputIntent.HideArrow(), new InputIntent.Deselect(4));
            Assert.That(m.Selected, Is.Null);
            Down(m, 1); Up(m, 1);
            Expect(m.Deselect(), new InputIntent.Deselect(1));
            Assert.That(m.Deselect(), Is.Empty);
        }

        [Test]
        public void Handle_dispatches_the_event_records()
        {
            var m = Make();
            Expect(m.Handle(new InputEvent.PointerDown(4, 0, 0, 0)), new InputIntent.Select(4));
            Expect(m.Handle(new InputEvent.PointerMove(5, 0, 0)), new InputIntent.ShowArrow(4, 5));
            Expect(m.Handle(new InputEvent.PointerUp(5, 0, 0)), new InputIntent.HideArrow(), new InputIntent.Deselect(4), new InputIntent.Swap(4, 5));
        }
    }
}
