using System.Collections;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Presentation;
using Cascade.Presentation.Shell;
using Cascade.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cascade.Tests.Play
{
    /// <summary>
    /// U4 acceptance: the Boot scene plays a generated level with real pointer events; after every move the view equals
    /// the session's board; screenshots of the real frame go to Builds/shots for review.
    /// </summary>
    public class PlayTests
    {
        PlayHarness h = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            h = new PlayHarness();
            yield return h.SetUpInLevel();
        }

        [UnityTearDown]
        public IEnumerator TearDown() => h.TearDown();

        [UnityTest]
        public IEnumerator A_level_is_shown_with_tray_pieces_and_hud()
        {
            h.AssertViewMatches("at the start");
            Assert.That(h.View.PieceCount, Is.EqualTo(h.Session.Board.Occupied().Count));
            Assert.That(h.App.Rig.SlotPx.Width, Is.GreaterThan(100), "the board slot was measured");
            Assert.That(h.Hud.MovesLabel.text, Is.EqualTo(h.Session.MovesLeft.ToString()));
            Assert.That(h.Ui.IsPauseShown, Is.False, "no pause overlay while playing");
            Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Play));
            yield return h.Shot("01_start");
        }

        [UnityTest]
        public IEnumerator A_mouse_drag_swap_animates_then_the_view_matches_the_session()
        {
            var (a, b) = h.FirstLegal();
            int movesBefore = h.Session.MovesLeft;
            yield return h.Press(h.ScreenOf(a));
            yield return PlayHarness.Seconds(0.2f);
            Assert.That(h.View.Get(h.Session.Board.TileAt(a)!.Id)!.SelectT, Is.GreaterThan(0.9f), "the pressed piece is lifted");
            yield return h.Shot("02_pressed_selected");
            // drag into the neighbor: the arrow shows
            var pa = h.ScreenOf(a); var pb = h.ScreenOf(b);
            yield return h.MoveMouse(Vector2.Lerp(pa, pb, 0.5f));
            yield return h.MoveMouse(pb);
            Assert.That(h.View.ArrowVisible, Is.True, "arrow while the pointer is inside the neighbor");
            yield return h.Shot("03_drag_arrow");
            yield return h.Release();
            Assert.That(h.View.ArrowVisible, Is.False);
            Assert.That(h.Controller.IsAnimating, Is.True, "the swap is animating");
            Assert.That(h.Session.MovesLeft, Is.EqualTo(movesBefore - 1));
            yield return PlayHarness.Seconds(0.12f);
            yield return h.Shot("04_swap_mid");
            yield return PlayHarness.Seconds(0.35f);
            Debug.Log("[PlayTest] popups " + h.Hud.PopupLayer.childCount + (h.Hud.PopupLayer.childCount > 0 ? " first at " + h.Hud.PopupLayer[0].resolvedStyle.left + "," + h.Hud.PopupLayer[0].resolvedStyle.top + " opacity " + h.Hud.PopupLayer[0].resolvedStyle.opacity + " size " + h.Hud.PopupLayer[0].worldBound : ""));
            yield return h.Shot("05_after_swap_clears");
            yield return h.Settled();
            h.AssertViewMatches("after the swap");
            yield return h.Shot("06_settled");
        }

        [UnityTest]
        public IEnumerator A_click_click_swap_that_makes_no_match_bounces_back_and_costs_no_move()
        {
            var pair = h.FindIllegal();
            Assert.That(pair, Is.Not.Null);
            var (a, b) = pair!.Value;
            int moves = h.Session.MovesLeft;
            var before = h.Session.Board;
            yield return h.Click(h.ScreenOf(a));
            yield return PlayHarness.Seconds(0.2f);
            yield return h.Click(h.ScreenOf(b));
            Assert.That(h.Controller.IsAnimating, Is.True, "the rejected swap animates");
            yield return PlayHarness.Seconds(0.1f);
            yield return h.Shot("07_rejected_mid");
            yield return h.Settled();
            Assert.That(h.Session.MovesLeft, Is.EqualTo(moves));
            Assert.That(h.Session.Board, Is.SameAs(before));
            h.AssertViewMatches("after a rejected swap");
        }

        [UnityTest]
        public IEnumerator Clicking_the_selected_piece_again_deselects_and_another_piece_moves_the_selection()
        {
            var a = h.Session.Board.Topology.Cells[0];
            var far = h.Session.Board.Topology.Cells[h.Session.Board.Topology.Cells.Count - 1];
            yield return h.Click(h.ScreenOf(a));
            Assert.That(h.Controller.Selected, Is.EqualTo(a));
            yield return h.Click(h.ScreenOf(far));
            Assert.That(h.Controller.Selected, Is.EqualTo(far), "a click on any other piece moves the selection");
            yield return h.Click(h.ScreenOf(far));
            Assert.That(h.Controller.Selected, Is.Null, "a click on the selected piece deselects it");
            yield return PlayHarness.Seconds(0.2f);
            foreach (int id in h.View.TileIds) Assert.That(h.View.Get(id)!.SelectT, Is.EqualTo(0f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator A_swap_requested_while_animating_waits_for_the_board_to_settle()
        {
            var (a, b) = h.FirstLegal();
            yield return h.Click(h.ScreenOf(a));
            yield return h.Click(h.ScreenOf(b));
            Assert.That(h.Controller.IsAnimating, Is.True);
            int moves = h.Session.MovesLeft;
            // request another swap while animating: it is queued, not played
            h.Controller.RequestSwap(a, b);
            Assert.That(h.Controller.HasPendingSwap, Is.True);
            Assert.That(h.Session.MovesLeft, Is.EqualTo(moves), "nothing played yet");
            yield return h.Settled();
            yield return PlayHarness.Seconds(0.2f);
            // the queued swap was played (or rejected) after the settle; either way the view matches when idle again
            yield return h.Settled();
            Assert.That(h.Controller.HasPendingSwap, Is.False);
            h.AssertViewMatches("after the queued swap");
        }

        [UnityTest]
        public IEnumerator Wheel_zoom_right_drag_pan_and_double_click_on_empty_space_reset()
        {
            var cam = h.App.Rig.Board!;
            var slot = h.App.Rig.SlotPx;
            var middle = h.ScreenOf(slot.Center);
            Assert.That(cam.Zoom, Is.EqualTo(1.0).Within(1e-9));
            yield return h.Scroll(middle, 3);
            Assert.That(cam.Zoom, Is.GreaterThan(1.2), "wheel zooms in");
            Assert.That(cam.UserZoomed, Is.True);
            yield return h.Shot("08_zoomed");
            var before = cam.PanOffset;
            yield return h.RightDrag(middle, middle + new Vector2(120, 60));
            Assert.That(cam.PanOffset.Distance(before), Is.GreaterThan(0.1), "right drag pans");
            yield return h.Shot("09_panned");
            // double click on empty space (a corner of the slot, outside the zoomed board's cells is not guaranteed: use zoom out first)
            yield return h.Scroll(middle, -8);
            Assert.That(cam.Zoom, Is.LessThan(1.0), "zoomed out below auto-fit (zoom_min)");
            var corner = h.ScreenOf(new Float2(slot.X + 4, slot.Y + 4));
            yield return h.Click(corner);
            yield return PlayHarness.Seconds(0.05f);
            yield return h.Click(corner);
            yield return PlayHarness.Frames(2);
            Assert.That(cam.Zoom, Is.EqualTo(1.0).Within(1e-9), "a double click on empty space resets the view");
            Assert.That(cam.UserZoomed, Is.False);
            yield return h.Shot("10_reset");
        }

        [UnityTest]
        public IEnumerator Pause_freezes_the_animation_and_resume_continues_it()
        {
            var (a, b) = h.FirstLegal();
            yield return h.Drag(a, b);
            yield return PlayHarness.Seconds(0.15f);
            h.Controller.Paused = true;
            var snap = Snapshot();
            yield return PlayHarness.Seconds(0.4f);
            Assert.That(Snapshot(), Is.EqualTo(snap), "no piece moves while paused");
            Assert.That(h.Controller.IsAnimating, Is.True);
            h.Controller.Paused = false;
            yield return h.Settled();
            h.AssertViewMatches("after pause and resume");
        }

        string Snapshot()
        {
            var sb = new System.Text.StringBuilder();
            var ids = new List<int>(h.View.TileIds);
            ids.Sort();
            foreach (int id in ids) sb.Append(id).Append(':').Append(h.View.Get(id)!.BasePosition).Append(h.View.Get(id)!.Sink).Append(';');
            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator Hint_button_pulses_the_suggested_move()
        {
            yield return h.ClickElement(h.Hud.HintButton);
            yield return PlayHarness.Seconds(0.3f);
            var hint = h.Session.Hint()!.Value;
            var va = h.View.Get(h.Session.Board.TileAt(hint.A)!.Id)!;
            Assert.That(va.Hover, Is.GreaterThan(0.1f), "the hinted piece is bouncing");
            yield return h.Shot("11_hint");
        }

        [UnityTest]
        public IEnumerator A_level_is_played_to_the_end_by_mouse_and_the_result_page_follows_the_last_animation()
        {
            int moves = 0;
            bool shotMid = false;
            while (h.Session.IsPlaying && moves < 80)
            {
                var hint = h.Session.Hint();
                Assert.That(hint, Is.Not.Null, "a playing level has a legal move");
                var (a, b) = hint!.Value;
                yield return h.Drag(a, b, 4);
                moves++;
                if (!shotMid && moves == 2)
                {
                    shotMid = true;
                    yield return PlayHarness.Seconds(0.45f);
                    yield return h.Shot("12_cascade_mid");
                }
                if (h.Session.IsPlaying)
                {
                    yield return h.Settled();
                    h.AssertViewMatches("after move " + moves);
                    Assert.That(h.Hud.MovesLabel.text, Is.EqualTo(h.Session.MovesLeft.ToString()), "HUD moves after move " + moves);
                    Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Play), "the result is not shown while the level goes on");
                }
                yield return PlayHarness.Frames(1);
            }
            Assert.That(h.Session.IsPlaying, Is.False, "the level ended within the move limit");
            // the result page waits for the last animation: the flow is still on Play while the board animates
            if (h.Controller.IsAnimating) Assert.That(h.Flow.Current, Is.EqualTo(ScreenId.Play), "the result waits for the last animation");
            yield return h.WaitForResult();
            Assert.That(h.Flow.Won, Is.EqualTo(h.Session.Status == SessionStatus.Won));
            yield return h.Shot("13_result_" + h.Session.Status);
        }

        [UnityTest]
        public IEnumerator A_later_level_has_holes_and_specials_that_animate_with_washes()
        {
            yield return h.StartLevel(60, 777);
            yield return PlayHarness.Frames(3);
            yield return h.Shot("15_level60_start");
            int shots = 0;
            int firedShots = 0;
            for (int i = 0; i < 40 && h.Session.IsPlaying; i++)
            {
                var hint = h.Session.Hint();
                if (hint == null) break;
                var (a, b) = hint.Value;
                var board = h.Session.Board;
                // peek: does this move fire a special? (a copy of the session answers without touching the real one)
                var peek = h.Session.Copy().TrySwap(a, b);
                bool fires = false, creates = false;
                foreach (var e in peek.Events) { if (e is SpecialFired) fires = true; if (e is SpecialCreated) creates = true; }
                yield return h.Drag(a, b, 3);
                if (fires && firedShots < 3)
                {
                    firedShots++;
                    yield return PlayHarness.Seconds(0.35f);
                    yield return h.Shot("16_special_fire_" + firedShots);
                    yield return PlayHarness.Seconds(0.2f);
                    yield return h.Shot("16_special_fire_" + firedShots + "b");
                }
                else if (creates && shots < 1)
                {
                    shots++;
                    yield return PlayHarness.Seconds(0.8f);
                    yield return h.Shot("17_special_created");
                }
                yield return h.Settled();
                if (h.Flow.Current == ScreenId.Play) h.AssertViewMatches("level 60 move " + (i + 1));
            }
            yield return h.Shot("18_level60_later");
        }

        [UnityTest]
        public IEnumerator The_tall_layout_is_chosen_for_a_portrait_window_and_the_board_refits()
        {
            var wideSlot = h.App.Rig.SlotPx;
            h.Resize(1080, 1920);
            yield return PlayHarness.Seconds(0.3f);
            Assert.That(h.Flow.Layout, Is.EqualTo(LayoutClass.Tall), "portrait picks layout--tall");
            Assert.That(h.Ui.Shell.ClassListContains("layout--tall"), Is.True);
            var slot = h.App.Rig.SlotPx;
            Assert.That(slot.Height, Is.GreaterThan(slot.Width * 0.8), "the tall layout gives the board a tall slot");
            Assert.That(slot.Width, Is.GreaterThan(wideSlot.Width * 0.9));
            Assert.That(h.App.Rig.Board!.UserZoomed, Is.False);
            yield return h.Shot("19_tall_1080x1920");
            var (a, b) = h.FirstLegal();
            yield return h.Drag(a, b);
            yield return PlayHarness.Seconds(0.4f);
            yield return h.Shot("20_tall_mid_move");
            yield return h.Settled();
            h.AssertViewMatches("after a move in the tall layout");
        }

        [UnityTest]
        public IEnumerator A_shaped_board_with_holes_shows_the_tray_around_them()
        {
            int level = 0; ulong seed = 4242;
            for (int n = 30; n <= 100 && level == 0; n++)
                if (h.App.Levels.Get(n, seed).Holes.Count > 0) level = n;
            Assert.That(level, Is.GreaterThan(0), "some level between 30 and 100 has holes");
            yield return h.StartLevel(level, seed);
            yield return PlayHarness.Seconds(0.2f);
            h.AssertViewMatches("on a board with holes");
            yield return h.Shot("21_holes_level" + level);
            var (a, b) = h.FirstLegal();
            yield return h.Drag(a, b);
            yield return h.Settled();
            h.AssertViewMatches("after a move on a board with holes");
        }
    }
}
