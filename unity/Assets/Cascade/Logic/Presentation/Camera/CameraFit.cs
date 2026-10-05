using System;
using System.Collections.Generic;
using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>The cell under a screen pixel and where in the cell: offsets in cell units from the cell's middle, -0.5..0.5.</summary>
    public readonly struct CellPick
    {
        public CellPick(CellId cell, double fx, double fy, Float2 world) { Cell = cell; Fx = fx; Fy = fy; World = world; }
        public CellId Cell { get; }
        /// <summary>Offset to the right (+X).</summary>
        public double Fx { get; }
        /// <summary>Offset toward the bottom of the screen (-Z, the direction rows count).</summary>
        public double Fy { get; }
        /// <summary>The picked point on the pick plane, board-space (X, Z).</summary>
        public Float2 World { get; }
    }

    /// <summary>
    /// Pure camera math for the board (TRD 12.2): auto-fit, projection and picking. Conventions are on
    /// <see cref="CameraPose"/>. <see cref="BoardCamera"/> adds the zoom / pan state on top of
    /// <see cref="Fit(IReadOnlyList{Float2}, RectF, Float2, CameraParams)"/>.
    /// </summary>
    public static class CameraFit
    {
        /// <summary>Auto-fit a full rectangular grid (cells 0..cols-1 by 0..rows-1).</summary>
        public static CameraPose Fit(int cols, int rows, RectF slotPx, Float2 screenPx, CameraParams p)
        {
            var centers = new List<Float2>();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    centers.Add(BoardGeometry.CellToWorld(x, y, cols, rows));
            return Fit(centers, slotPx, screenPx, p);
        }

        /// <summary>Auto-fit the cells of a board (holes are simply absent).</summary>
        public static CameraPose Fit(BoardGeometry geometry, RectF slotPx, Float2 screenPx, CameraParams p)
        {
            var centers = new List<Float2>();
            foreach (var c in geometry.Cells) centers.Add(geometry.CellPosition(c));
            return Fit(centers, slotPx, screenPx, p);
        }

        /// <summary>Same with the numbers spelled out (and the shape constants of <see cref="CameraParams.Default"/>).</summary>
        public static CameraPose Fit(IReadOnlyList<Float2> cellCenters, RectF slotPx, Float2 screenPx, double fovDeg, double pitchDeg, double fitMargin)
            => Fit(cellCenters, slotPx, screenPx, CameraParams.Default with { FovDeg = fovDeg, PitchDeg = pitchDeg, FitMargin = fitMargin });

        /// <summary>
        /// The camera that shows the whole board (its cells, the tray lip and depth, and the height of a lifted
        /// piece) as large as possible inside <paramref name="slotPx"/>: it fills <c>1 - FitMargin</c> of the slot width or height, whichever limits,
        /// centered on the slot's center pixel. Solved exactly for the perspective projection (a bisection on the
        /// distance and a fixed-point iteration on the shift), so it holds for any slot shape.
        /// </summary>
        /// <param name="cellCenters">Board-space (X, Z) of every cell's middle.</param>
        public static CameraPose Fit(IReadOnlyList<Float2> cellCenters, RectF slotPx, Float2 screenPx, CameraParams p)
        {
            if (cellCenters.Count == 0) throw new ArgumentException("no cells to fit", nameof(cellCenters));
            if (slotPx.Width < 1 || slotPx.Height < 1) slotPx = new RectF(slotPx.X, slotPx.Y, Math.Max(1, slotPx.Width), Math.Max(1, slotPx.Height));

            double half = 0.5 + p.TrayLip;
            var pts = new List<Float3>(cellCenters.Count * 8);
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var c in cellCenters)
            {
                minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                minZ = Math.Min(minZ, c.Y); maxZ = Math.Max(maxZ, c.Y);
                foreach (double dx in new[] { -half, half })
                    foreach (double dz in new[] { -half, half })
                    {
                        pts.Add(new Float3(c.X + dx, -p.TrayDepth, c.Y + dz));
                        pts.Add(new Float3(c.X + dx, p.PieceTop, c.Y + dz));
                    }
            }

            double keep = Math.Max(0.05, 1.0 - p.FitMargin); // the board fills (1 - margin) of the slot in the limiting direction
            double targetW = slotPx.Width * keep, targetH = slotPx.Height * keep;

            Func<Float2, double, CameraPose> pose = (focus, dist) => CameraPose.Create(focus, dist, slotPx, screenPx, p.FovDeg, p.PitchDeg);

            // projected pixel bounds of the board for a pose; null if a point is behind the camera
            Func<CameraPose, (double X0, double Y0, double X1, double Y1)?> bounds = cam =>
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                foreach (var pt in pts)
                {
                    var s = cam.Project(pt);
                    if (double.IsNaN(s.X)) return null;
                    x0 = Math.Min(x0, s.X); x1 = Math.Max(x1, s.X);
                    y0 = Math.Min(y0, s.Y); y1 = Math.Max(y1, s.Y);
                }
                return (x0, y0, x1, y1);
            };

            // how much too big the board is at this distance (1 = exactly fills the target; more = too big)
            Func<Float2, double, double> ratio = (focus, dist) =>
            {
                var b = bounds(pose(focus, dist));
                if (b == null) return double.PositiveInfinity;
                return Math.Max((b.Value.X1 - b.Value.X0) / targetW, (b.Value.Y1 - b.Value.Y0) / targetH);
            };

            Func<Float2, double> solveDistance = focus =>
            {
                double lo = 0.01, hi = 2000.0; // ratio is decreasing in distance
                for (int i = 0; i < 70; i++)
                {
                    double mid = Math.Sqrt(lo * hi);
                    if (ratio(focus, mid) > 1.0) lo = mid; else hi = mid;
                }
                return hi;
            };

            var f = new Float2((minX + maxX) / 2, (minZ + maxZ) / 2);
            double d = solveDistance(f);
            for (int iter = 0; iter < 30; iter++)
            {
                var cam = pose(f, d);
                var b = bounds(cam);
                if (b == null) break;
                var boardCenterPx = new Float2((b.Value.X0 + b.Value.X1) / 2, (b.Value.Y0 + b.Value.Y1) / 2);
                var onBoard = cam.GroundPoint(boardCenterPx, p.PickHeight);
                var onSlot = cam.GroundPoint(slotPx.Center, p.PickHeight);
                if (onBoard == null || onSlot == null) break;
                var shift = onBoard.Value - onSlot.Value;
                f = f + shift;
                d = solveDistance(f);
                if (shift.Length < 1e-9) break;
            }
            return pose(f, d);
        }

        /// <summary>Screen pixel of a world point (NaN components when behind the camera).</summary>
        public static Float2 Project(CameraPose pose, Float3 world) => pose.Project(world);

        /// <summary>Screen pixel of the middle of a cell at the picking height.</summary>
        public static Float2 Project(CameraPose pose, BoardGeometry geometry, CellId cell, CameraParams p)
            => pose.Project(geometry.CellPosition(cell), p.PickHeight);

        /// <summary>
        /// The cell under a screen pixel: the ray from the camera through the pixel meets the plane
        /// <c>Y = PickHeight</c> (about the middle of a piece); null over a hole, outside the board, or (when
        /// <paramref name="requireInsideSlot"/>) outside the slot rect, so taps on the HUD never pick.
        /// </summary>
        public static CellPick? Pick(CameraPose pose, BoardGeometry geometry, Float2 px, CameraParams p, bool requireInsideSlot = true)
        {
            if (requireInsideSlot && !pose.SlotPx.Contains(px)) return null;
            var hit = pose.GroundPoint(px, p.PickHeight);
            if (hit == null) return null;
            if (!geometry.TryCellAt(hit.Value, out var cell, out double fx, out double fy)) return null;
            return new CellPick(cell, fx, fy, hit.Value);
        }
    }
}
