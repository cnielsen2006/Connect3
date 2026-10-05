using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Cascade.Presentation;
using Cascade.Tests.CoreTests;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 9.1 / 12.2: fit, projection, picking, zoom, pan, reset and slot changes of the board camera.</summary>
    public class CameraTests
    {
        static readonly CameraParams P = CameraParams.Default;

        static readonly (string Name, RectF Slot, Float2 Screen)[] Layouts =
        {
            ("phone portrait", new RectF(0, 260, 1080, 1100), new Float2(1080, 1920)),
            ("phone portrait, tall slot", new RectF(40, 200, 1000, 1500), new Float2(1080, 1920)),
            ("desktop wide", new RectF(300, 100, 1000, 700), new Float2(1600, 900)),
            ("wide strip", new RectF(0, 0, 1600, 500), new Float2(1600, 900)),
            ("narrow column", new RectF(0, 0, 500, 1400), new Float2(500, 1400)),
            ("square", new RectF(100, 100, 700, 700), new Float2(900, 900)),
        };

        static BoardGeometry Grid(int n) => BoardGeometry.FromTopology(Topology.Grid(n, n));

        static IEnumerable<(int N, string Name, RectF Slot, Float2 Screen)> Cases()
        {
            foreach (int n in new[] { 6, 7, 8, 9 })
                foreach (var l in Layouts)
                    yield return (n, l.Name, l.Slot, l.Screen);
        }

        /// <summary>The points the fit keeps inside the slot: the tray box around every cell.</summary>
        static List<Float3> FitPoints(BoardGeometry g)
        {
            double h = 0.5 + P.TrayLip;
            var pts = new List<Float3>();
            foreach (var c in g.Cells)
            {
                var p = g.CellPosition(c);
                foreach (double dx in new[] { -h, h })
                    foreach (double dz in new[] { -h, h })
                    {
                        pts.Add(new Float3(p.X + dx, -P.TrayDepth, p.Y + dz));
                        pts.Add(new Float3(p.X + dx, P.PieceTop, p.Y + dz));
                    }
            }
            return pts;
        }

        static (double X0, double Y0, double X1, double Y1) Bounds(CameraPose pose, IEnumerable<Float3> pts)
        {
            var s = pts.Select(pose.Project).ToList();
            return (s.Min(v => v.X), s.Min(v => v.Y), s.Max(v => v.X), s.Max(v => v.Y));
        }

        // ---- conventions --------------------------------------------------------------------------------------

        [Test]
        public void Pose_conventions_three_quarter_view_looking_down_toward_plus_z()
        {
            var pose = CameraFit.Fit(7, 7, new RectF(0, 0, 800, 800), new Float2(800, 800), P);
            Assert.That(pose.EulerDegrees.X, Is.EqualTo(65).Within(1e-9), "90 - pitch");
            Assert.That(pose.EulerDegrees.Y, Is.EqualTo(0));
            Assert.That(pose.EulerDegrees.Z, Is.EqualTo(0));
            Assert.That(pose.FovDeg, Is.EqualTo(30));
            Assert.That(pose.Forward.Y, Is.LessThan(0), "looks down");
            Assert.That(pose.Forward.Z, Is.GreaterThan(0), "and toward +Z");
            Assert.That(pose.Forward.Dot(pose.Up), Is.EqualTo(0).Within(1e-12));
            Assert.That(pose.Forward.Dot(pose.Right), Is.EqualTo(0).Within(1e-12));
            Assert.That(pose.Up.Z, Is.GreaterThan(0), "+Z is up the screen");
            Assert.That(pose.Position.Y, Is.GreaterThan(0));
            Assert.That(pose.Position.Z, Is.LessThan(pose.Focus.Y), "the camera sits on the -Z side");
            // +Z is up on the screen, +X is right
            var c = pose.Project(new Float3(pose.Focus.X, 0, pose.Focus.Y));
            Assert.That(pose.Project(new Float3(pose.Focus.X, 0, pose.Focus.Y + 1)).Y, Is.LessThan(c.Y));
            Assert.That(pose.Project(new Float3(pose.Focus.X + 1, 0, pose.Focus.Y)).X, Is.GreaterThan(c.X));
        }

        [Test]
        public void Pitch_zero_is_straight_down()
        {
            var pose = CameraFit.Fit(6, 6, new RectF(0, 0, 900, 900), new Float2(900, 900), P with { PitchDeg = 0 });
            Assert.That(pose.EulerDegrees.X, Is.EqualTo(90).Within(1e-12));
            Assert.That(pose.Forward.Near(new Float3(0, -1, 0), 1e-12));
            Assert.That(pose.Position.X, Is.EqualTo(0).Within(1e-6));
            Assert.That(pose.Position.Z, Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void A_point_behind_the_camera_does_not_project()
        {
            var pose = CameraFit.Fit(6, 6, new RectF(0, 0, 900, 900), new Float2(900, 900), P);
            var behind = pose.Position - pose.Forward * 3.0;
            Assert.That(double.IsNaN(pose.Project(behind).X));
        }

        // ---- fit ----------------------------------------------------------------------------------------------

        [Test]
        public void Fit_fills_the_slot_minus_the_margin_and_centers_the_board_for_every_size_and_slot_shape()
        {
            foreach (var (n, name, slot, screen) in Cases())
            {
                var g = Grid(n);
                var pose = CameraFit.Fit(g, slot, screen, P);
                var b = Bounds(pose, FitPoints(g));
                double w = b.X1 - b.X0, h = b.Y1 - b.Y0;
                string label = $"{n}x{n} in {name}";
                double fill = Math.Max(w / slot.Width, h / slot.Height);
                Assert.That(fill, Is.EqualTo(1 - P.FitMargin).Within(0.002), label + ": fills 1 - margin in the limiting direction");
                Assert.That(w, Is.LessThanOrEqualTo(slot.Width * (1 - P.FitMargin) + 0.5), label);
                Assert.That(h, Is.LessThanOrEqualTo(slot.Height * (1 - P.FitMargin) + 0.5), label);
                Assert.That((b.X0 + b.X1) / 2, Is.EqualTo(slot.Center.X).Within(0.5), label + ": centered in x");
                Assert.That((b.Y0 + b.Y1) / 2, Is.EqualTo(slot.Center.Y).Within(0.5), label + ": centered in y");
                // all four flat corners of the board lie inside the slot
                foreach (var (cx, cz) in new[] { (-n / 2.0, -n / 2.0), (n / 2.0, -n / 2.0), (-n / 2.0, n / 2.0), (n / 2.0, n / 2.0) })
                    Assert.That(slot.Contains(pose.Project(new Float3(cx, 0, cz))), label + ": corner " + cx + "," + cz);
            }
        }

        [Test]
        public void A_bigger_board_needs_a_farther_camera_and_a_bigger_slot_a_nearer_one()
        {
            var screen = new Float2(1000, 1000);
            var slot = new RectF(0, 0, 1000, 1000);
            double last = 0;
            foreach (int n in new[] { 6, 7, 8, 9 })
            {
                double d = CameraFit.Fit(n, n, slot, screen, P).Distance;
                Assert.That(d, Is.GreaterThan(last));
                last = d;
            }
            var small = CameraFit.Fit(7, 7, new RectF(0, 0, 500, 500), screen, P);
            var large = CameraFit.Fit(7, 7, slot, screen, P);
            Assert.That(small.Distance, Is.GreaterThan(large.Distance), "a smaller slot shows the board smaller: the camera is farther");
        }

        [Test]
        public void Fit_with_holes_uses_only_the_present_cells()
        {
            // a ring of cells: the corner cells are holes, so the fit is tighter than for the full grid
            var rows = new[] { ". 0 1 2 .", "1 2 0 1 2", "0 1 2 0 1", "1 2 0 1 2", ". 1 2 0 ." };
            var g = BoardGeometry.FromTopology(T.BoardFrom(rows).Topology);
            Assert.That(g.Cells.Count, Is.EqualTo(21));
            var slot = new RectF(0, 0, 900, 900);
            var pose = CameraFit.Fit(g, slot, new Float2(900, 900), P);
            var b = Bounds(pose, FitPoints(g));
            Assert.That(Math.Max((b.X1 - b.X0) / 900, (b.Y1 - b.Y0) / 900), Is.EqualTo(1 - P.FitMargin).Within(0.002));
            Assert.That((b.X0 + b.X1) / 2, Is.EqualTo(450).Within(0.5));
            Assert.That((b.Y0 + b.Y1) / 2, Is.EqualTo(450).Within(0.5));
            var full = CameraFit.Fit(5, 5, slot, new Float2(900, 900), P);
            Assert.That(pose.Distance, Is.LessThanOrEqualTo(full.Distance + 1e-9), "never farther than the full grid");
        }

        [Test]
        public void An_off_center_cell_set_is_centered_on_the_slot_not_on_the_world_origin()
        {
            var cells = new List<Float2> { new Float2(10, 4), new Float2(11, 4), new Float2(10, 5), new Float2(11, 5) };
            var slot = new RectF(100, 50, 700, 500);
            var pose = CameraFit.Fit(cells, slot, new Float2(900, 700), 30, 25, 0.04);
            Assert.That(pose.Focus.X, Is.EqualTo(10.5).Within(0.3));
            var pts = cells.SelectMany(c => new[] { new Float3(c.X - 0.5, 0, c.Y - 0.5), new Float3(c.X + 0.5, 0, c.Y + 0.5) });
            var b = Bounds(pose, pts);
            Assert.That((b.X0 + b.X1) / 2, Is.EqualTo(slot.Center.X).Within(15), "roughly centered (the tray box, not the flat corners, is what is centered exactly)");
        }

        // ---- picking ------------------------------------------------------------------------------------------

        [Test]
        public void Project_and_Pick_round_trip_with_offsets_inside_every_cell()
        {
            foreach (int n in new[] { 6, 9 })
                foreach (var (name, slot, screen) in Layouts)
                {
                    var g = Grid(n);
                    var pose = CameraFit.Fit(g, slot, screen, P);
                    foreach (var cell in g.Cells)
                        foreach (var (dx, dz) in new[] { (0.0, 0.0), (0.3, -0.2), (-0.45, 0.45), (0.1, 0.4) })
                        {
                            var w = g.CellPosition(cell);
                            var px = pose.Project(new Float2(w.X + dx, w.Y + dz), P.PickHeight);
                            var hit = CameraFit.Pick(pose, g, px, P);
                            Assert.That(hit, Is.Not.Null, $"{n}x{n} {name} cell {cell} ({dx},{dz})");
                            Assert.That(hit!.Value.Cell, Is.EqualTo(cell));
                            Assert.That(hit.Value.Fx, Is.EqualTo(dx).Within(1e-6));
                            Assert.That(hit.Value.Fy, Is.EqualTo(-dz).Within(1e-6), "Fy counts toward the bottom of the screen (-Z)");
                            Assert.That(hit.Value.World.Near(new Float2(w.X + dx, w.Y + dz), 1e-6), Is.True);
                        }
                }
        }

        [Test]
        public void Pick_returns_null_over_a_hole_outside_the_board_and_outside_the_slot()
        {
            var b = T.BoardFrom(new[] { "0 1 2", "1 . 0", "2 0 1" });
            var g = BoardGeometry.FromTopology(b.Topology);
            var slot = new RectF(100, 100, 600, 600);
            var screen = new Float2(800, 800);
            var pose = CameraFit.Fit(g, slot, screen, P);
            var holeCenter = pose.Project(new Float2(0, 0), P.PickHeight);
            Assert.That(CameraFit.Pick(pose, g, holeCenter, P), Is.Null, "the hole");
            Assert.That(CameraFit.Pick(pose, g, pose.Project(new Float2(-1, 1), P.PickHeight), P), Is.Not.Null);

            var offBoard = new Float2(slot.Center.X, slot.Y + 2); // inside the slot, above the board
            Assert.That(CameraFit.Pick(pose, g, offBoard, P), Is.Null, "off the board");
            Assert.That(CameraFit.Pick(pose, g, new Float2(5, 5), P), Is.Null, "outside the slot");
            var px = pose.Project(new Float2(-1, 1), P.PickHeight);
            var narrow = CameraPose.Create(pose.Focus, pose.Distance, new RectF(350, 350, 100, 100), screen, 30, 25);
            Assert.That(narrow.SlotPx.Contains(narrow.Project(new Float2(-1, 1), P.PickHeight)), Is.False, "the cell is outside the narrow slot");
            Assert.That(CameraFit.Pick(narrow, g, narrow.Project(new Float2(-1, 1), P.PickHeight), P), Is.Null);
            Assert.That(CameraFit.Pick(narrow, g, narrow.Project(new Float2(-1, 1), P.PickHeight), P, requireInsideSlot: false), Is.Not.Null);
            Assert.That(px.X, Is.GreaterThan(0));
        }

        [Test]
        public void The_slot_center_picks_a_cell_near_the_middle_of_the_board()
        {
            foreach (var (n, name, slot, screen) in Cases())
            {
                var g = Grid(n);
                var pose = CameraFit.Fit(g, slot, screen, P);
                var hit = CameraFit.Pick(pose, g, slot.Center, P);
                Assert.That(hit, Is.Not.Null, $"{n} {name}");
                var (x, y) = g.LogicalOf(hit!.Value.Cell);
                Assert.That(Math.Abs(x - (n - 1) / 2.0), Is.LessThanOrEqualTo(1.5), $"{n} {name} column");
                Assert.That(Math.Abs(y - (n - 1) / 2.0), Is.LessThanOrEqualTo(1.5), $"{n} {name} row");
            }
        }

        [Test]
        public void GroundPoint_and_Project_are_inverse_on_any_plane()
        {
            var pose = CameraFit.Fit(8, 8, new RectF(0, 100, 1080, 1200), new Float2(1080, 1920), P);
            foreach (double y in new[] { -0.25, 0, 0.17, 0.45 })
            {
                var px = new Float2(300, 700);
                var g = pose.GroundPoint(px, y)!.Value;
                Assert.That(pose.Project(g, y).Near(px, 1e-6), Is.True);
            }
            Assert.That(pose.GroundPoint(new Float2(540, -100000), 0.17), Is.Null, "a ray that points above the horizon never hits the plane");
        }

        // ---- BoardCamera: zoom, pan, reset, viewport ----------------------------------------------------------------

        static BoardCamera MakeCamera(int n = 8, CameraParams? p = null)
        {
            var cam = new BoardCamera(Grid(n), p ?? P);
            cam.SetViewport(new RectF(40, 300, 1000, 1000), new Float2(1080, 1920));
            return cam;
        }

        [Test]
        public void The_camera_needs_a_viewport_first()
        {
            var cam = new BoardCamera(Grid(6), P);
            Assert.Throws<InvalidOperationException>(() => { var _ = cam.Pose; });
        }

        [Test]
        public void A_fresh_camera_shows_the_auto_fit()
        {
            var cam = MakeCamera();
            Assert.That(cam.Zoom, Is.EqualTo(1));
            Assert.That(cam.UserZoomed, Is.False);
            Assert.That(cam.Pose.Position.Near(cam.FitPose.Position, 1e-12), Is.True);
            var direct = CameraFit.Fit(cam.Geometry, new RectF(40, 300, 1000, 1000), new Float2(1080, 1920), P);
            Assert.That(cam.Pose.Position.Near(direct.Position, 1e-9), Is.True);
        }

        [Test]
        public void Wheel_zoom_steps_by_zoom_step_per_notch_and_clamps_to_the_limits()
        {
            var cam = MakeCamera();
            cam.ZoomNotches(2, cam.Pose.SlotPx.Center);
            Assert.That(cam.Zoom, Is.EqualTo(P.ZoomStep * P.ZoomStep).Within(1e-9));
            Assert.That(cam.UserZoomed, Is.True);
            for (int i = 0; i < 60; i++) cam.ZoomNotches(1, cam.Pose.SlotPx.Center);
            Assert.That(cam.Zoom, Is.EqualTo(P.ZoomMax).Within(1e-12));
            for (int i = 0; i < 120; i++) cam.ZoomNotches(-1, cam.Pose.SlotPx.Center);
            Assert.That(cam.Zoom, Is.EqualTo(P.ZoomMin).Within(1e-12));
            Assert.That(cam.Pose.Distance, Is.EqualTo(cam.FitPose.Distance / P.ZoomMin).Within(1e-9));
        }

        [Test]
        public void Zoom_keeps_the_point_under_the_cursor_or_pinch_center_in_place()
        {
            var cam = MakeCamera();
            foreach (var around in new[] { new Float2(540, 800), new Float2(200, 450), new Float2(900, 1150) })
            {
                cam.Reset();
                var before = cam.Pose.GroundPoint(around, P.PickHeight)!.Value;
                cam.ZoomBy(1.4, around);
                var after = cam.Pose.GroundPoint(around, P.PickHeight)!.Value;
                Assert.That(after.Near(before, 1e-6), Is.True, "the world point stays under " + around);
                Assert.That(cam.Pose.Project(before, P.PickHeight).Near(around, 1e-6), Is.True);
            }
        }

        [Test]
        public void Zooming_toward_a_cell_zooms_in_on_that_cell_not_the_middle()
        {
            var cam = MakeCamera();
            var corner = cam.ProjectCell(cam.Geometry.Cells[0]);
            cam.ZoomBy(2.0, corner);
            var hit = cam.Pick(corner);
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.Value.Cell, Is.EqualTo(cam.Geometry.Cells[0]));
            Assert.That(hit.Value.Fx, Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void Pan_moves_the_point_under_the_finger_with_the_finger()
        {
            var cam = MakeCamera();
            cam.ZoomBy(2.0, cam.Pose.SlotPx.Center);
            var from = new Float2(500, 700);
            var to = new Float2(560, 640);
            var world = cam.Pose.GroundPoint(from, P.PickHeight)!.Value;
            cam.Pan(from, to);
            Assert.That(cam.Pose.Project(world, P.PickHeight).Near(to, 1e-6), Is.True);
            Assert.That(cam.UserZoomed, Is.True);
        }

        [Test]
        public void PanBy_moves_the_content_by_the_pixel_delta_at_the_slot_center()
        {
            var cam = MakeCamera();
            cam.ZoomBy(2.0, cam.Pose.SlotPx.Center);
            var center = cam.Pose.SlotPx.Center;
            var world = cam.Pose.GroundPoint(center, P.PickHeight)!.Value;
            cam.PanBy(new Float2(30, -20));
            Assert.That(cam.Pose.Project(world, P.PickHeight).Near(center + new Float2(30, -20), 1e-6), Is.True);
        }

        [Test]
        public void Pan_is_clamped_so_the_board_stays_partly_visible()
        {
            var cam = MakeCamera(8);
            cam.ZoomBy(3.0, cam.Pose.SlotPx.Center);
            for (int i = 0; i < 50; i++) cam.PanBy(new Float2(-400, 400));
            Assert.That(Math.Abs(cam.PanOffset.X), Is.LessThanOrEqualTo(4.0 + 1e-9));
            Assert.That(Math.Abs(cam.PanOffset.Y), Is.LessThanOrEqualTo(4.0 + 1e-9));
            Assert.That(cam.Geometry.Cells.Any(c => cam.Pose.SlotPx.Contains(cam.ProjectCell(c))), "some cells are still on the slot at the clamp");
        }

        [Test]
        public void Reset_returns_to_the_auto_fit_and_clears_the_user_flag()
        {
            var cam = MakeCamera();
            var fit = cam.Pose.Position;
            cam.ZoomBy(2.2, new Float2(300, 500));
            cam.Pan(new Float2(500, 700), new Float2(420, 650));
            Assert.That(cam.Pose.Position.Near(fit, 1e-6), Is.False);
            cam.Reset();
            Assert.That(cam.Zoom, Is.EqualTo(1));
            Assert.That(cam.UserZoomed, Is.False);
            Assert.That(cam.PanOffset, Is.EqualTo(Float2.Zero));
            Assert.That(cam.Pose.Position.Near(fit, 1e-9), Is.True);
        }

        [Test]
        public void A_slot_change_refits_when_the_user_has_not_zoomed()
        {
            var cam = MakeCamera();
            var slot2 = new RectF(0, 0, 1600, 700);
            var screen2 = new Float2(1600, 900);
            cam.SetViewport(slot2, screen2);
            var direct = CameraFit.Fit(cam.Geometry, slot2, screen2, P);
            Assert.That(cam.Pose.Position.Near(direct.Position, 1e-9), Is.True);
            Assert.That(cam.Pose.SlotPx, Is.EqualTo(slot2));
        }

        [Test]
        public void A_slot_change_keeps_the_users_zoom_relative_to_the_new_fit()
        {
            var cam = MakeCamera();
            cam.ZoomBy(1.5, cam.Pose.SlotPx.Center);
            double zoom = cam.Zoom;
            var slot2 = new RectF(0, 0, 1600, 700);
            var screen2 = new Float2(1600, 900);
            cam.SetViewport(slot2, screen2);
            Assert.That(cam.UserZoomed, Is.True);
            Assert.That(cam.Zoom, Is.EqualTo(zoom).Within(1e-12));
            var fit = CameraFit.Fit(cam.Geometry, slot2, screen2, P);
            Assert.That(cam.Pose.Distance, Is.EqualTo(fit.Distance / zoom).Within(1e-9));
        }

        [Test]
        public void Setting_the_same_viewport_again_changes_nothing_and_a_new_board_resets_the_view()
        {
            var cam = MakeCamera(8);
            cam.ZoomBy(1.7, new Float2(300, 500));
            var pose = cam.Pose;
            cam.SetViewport(new RectF(40, 300, 1000, 1000), new Float2(1080, 1920));
            Assert.That(cam.Pose, Is.SameAs(pose));
            cam.SetBoard(Grid(6));
            Assert.That(cam.Zoom, Is.EqualTo(1));
            Assert.That(cam.UserZoomed, Is.False);
            Assert.That(cam.Pose.Distance, Is.EqualTo(CameraFit.Fit(6, 6, new RectF(40, 300, 1000, 1000), new Float2(1080, 1920), P).Distance).Within(1e-9));
        }

        [Test]
        public void Camera_dials_apply_live()
        {
            var cam = MakeCamera();
            var before = cam.Pose.Distance;
            cam.Params = P with { FitMargin = 0.2 };
            Assert.That(cam.Pose.Distance, Is.GreaterThan(before), "a bigger margin means a farther camera");
            cam.Params = P with { PitchDeg = 40 };
            Assert.That(cam.Pose.EulerDegrees.X, Is.EqualTo(50).Within(1e-12));
        }

        [Test]
        public void Pick_through_the_camera_matches_the_static_picker_and_projects_cells_inside_the_slot()
        {
            var cam = MakeCamera(7);
            foreach (var c in cam.Geometry.Cells)
            {
                var px = cam.ProjectCell(c);
                Assert.That(cam.Pose.SlotPx.Contains(px));
                Assert.That(cam.Pick(px)!.Value.Cell, Is.EqualTo(c));
            }
        }
    }
}
