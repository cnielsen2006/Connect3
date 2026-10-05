using System;
using System.Collections.Generic;
using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>
    /// Maps cells to board space. A <see cref="Float2"/> returned here is <c>(world X, world Z)</c>: the board
    /// lies in the XZ plane with Y up (TRD 12), one cell is one unit, and the piece sits on the tray at Y = 0.
    /// </summary>
    public interface ICellLayout
    {
        /// <summary>The world (X, Z) of the middle of a cell.</summary>
        Float2 CellPosition(CellId cell);

        /// <summary>The position <paramref name="rowsAbove"/> rows above a cell (toward the top of the screen, +Z): where a spawned piece starts.</summary>
        Float2 AbovePosition(CellId cell, double rowsAbove);
    }

    /// <summary>
    /// The grid layout of a board (and the cell set for picking). Cell (column x, row y; row 0 is the TOP row)
    /// is at world X = x - (cols-1)/2 and Z = -(y - (rows-1)/2). Holes are simply cells that are absent.
    /// Immutable; build one per level and share it between the planner, the camera and the view.
    /// </summary>
    public sealed class BoardGeometry : ICellLayout
    {
        readonly Float2[] _pos;
        readonly bool[] _present;
        readonly (int X, int Y)[] _logical;
        readonly Dictionary<(int, int), CellId> _byLogical = new Dictionary<(int, int), CellId>(); // lookup only
        readonly CellId[] _cells;

        BoardGeometry(IReadOnlyList<(CellId Cell, int X, int Y)> cells)
        {
            if (cells.Count == 0) throw new ArgumentException("a board needs at least one cell", nameof(cells));
            int cap = 0, maxX = 0, maxY = 0;
            foreach (var (cell, x, y) in cells)
            {
                cap = Math.Max(cap, cell.Value + 1);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
            Cols = maxX + 1;
            Rows = maxY + 1;
            _pos = new Float2[cap];
            _present = new bool[cap];
            _logical = new (int, int)[cap];
            var ids = new List<CellId>();
            foreach (var (cell, x, y) in cells)
            {
                if (_present[cell.Value]) throw new ArgumentException("duplicate cell " + cell);
                _present[cell.Value] = true;
                _pos[cell.Value] = CellToWorld(x, y, Cols, Rows);
                _logical[cell.Value] = (x, y);
                _byLogical[(x, y)] = cell;
                ids.Add(cell);
            }
            ids.Sort();
            _cells = ids.ToArray();
        }

        /// <summary>From a topology: every cell's logical (x, y) becomes its column and row.</summary>
        public static BoardGeometry FromTopology(Topology topology)
        {
            var list = new List<(CellId, int, int)>();
            foreach (var c in topology.Cells)
            {
                var p = topology.Pos(c);
                list.Add((c, p.X, p.Y));
            }
            return new BoardGeometry(list);
        }

        /// <summary>From explicit cells (cell id, column, row), for tests and tools.</summary>
        public static BoardGeometry FromCells(IEnumerable<(CellId Cell, int X, int Y)> cells)
            => new BoardGeometry(new List<(CellId, int, int)>(cells));

        /// <summary>World (X, Z) of logical cell (x, y) on a board of <paramref name="cols"/> x <paramref name="rows"/>.</summary>
        public static Float2 CellToWorld(int x, int y, int cols, int rows)
            => new Float2(x - (cols - 1) / 2.0, -(y - (rows - 1) / 2.0));

        public int Cols { get; }
        public int Rows { get; }

        /// <summary>Every cell, ascending by id.</summary>
        public IReadOnlyList<CellId> Cells => _cells;

        public bool Contains(CellId cell) => cell.Value >= 0 && cell.Value < _present.Length && _present[cell.Value];

        public Float2 CellPosition(CellId cell)
        {
            if (!Contains(cell)) throw new ArgumentException("cell " + cell + " is not on the board", nameof(cell));
            return _pos[cell.Value];
        }

        public Float2 AbovePosition(CellId cell, double rowsAbove)
        {
            var p = CellPosition(cell);
            return new Float2(p.X, p.Y + rowsAbove);
        }

        /// <summary>The (column, row) of a cell.</summary>
        public (int X, int Y) LogicalOf(CellId cell) => _logical[cell.Value];

        /// <summary>
        /// The cell containing the world point (X, Z), if any, and the offset of the point from the cell's
        /// middle in cell units: <paramref name="fx"/> to the right (+X), <paramref name="fy"/> toward the
        /// bottom of the screen (-Z, the direction rows count), both in -0.5..0.5.
        /// </summary>
        public bool TryCellAt(Float2 world, out CellId cell, out double fx, out double fy)
        {
            double lx = world.X + (Cols - 1) / 2.0;
            double ly = -world.Y + (Rows - 1) / 2.0;
            int cx = (int)Math.Floor(lx + 0.5), cy = (int)Math.Floor(ly + 0.5);
            fx = lx - cx;
            fy = ly - cy;
            return _byLogical.TryGetValue((cx, cy), out cell);
        }

        /// <summary>Outer edges of the cells in world units: (minX, minZ) to (maxX, maxZ), without the tray lip.</summary>
        public (double MinX, double MinZ, double MaxX, double MaxZ) WorldBounds()
        {
            double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
            foreach (var c in _cells)
            {
                var p = _pos[c.Value];
                x0 = Math.Min(x0, p.X - 0.5); x1 = Math.Max(x1, p.X + 0.5);
                z0 = Math.Min(z0, p.Y - 0.5); z1 = Math.Max(z1, p.Y + 0.5);
            }
            return (x0, z0, x1, z1);
        }
    }
}
