using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>An ordered sequence of cells along which matches are found (a row or a column on a grid).</summary>
    public sealed record CellLine(int Axis, ValueList<CellId> Cells);

    /// <summary>A cell's position inside one of the lines through it.</summary>
    public readonly struct LineSlot
    {
        public LineSlot(CellLine line, int index) { Line = line; Index = index; }
        public CellLine Line { get; }
        /// <summary>Index of the cell in <see cref="CellLine.Cells"/>.</summary>
        public int Index { get; }
    }

    /// <summary>
    /// Static board structure: cells, neighbors, lines, areas and links (TRD §3). Rules only ever
    /// ask this object; they never read coordinates. Holes are simply absent. Immutable once built.
    /// </summary>
    public sealed class Topology
    {
        readonly Pos[] _pos;
        readonly bool[] _present;
        readonly CellId[][] _neighbors;
        readonly LineSlot[][] _through;
        readonly Func<CellId, int, IReadOnlyList<CellId>>? _areaFn;
        readonly Dictionary<(int, int, int), CellId> _byPos = new Dictionary<(int, int, int), CellId>(); // lookup only

        /// <summary>
        /// Builds a topology. <paramref name="cells"/> gives every cell and its logical position;
        /// <paramref name="neighbors"/> the swap neighbors of cells (a cell not listed has none);
        /// <paramref name="lines"/> the match lines; <paramref name="links"/> directed non-adjacent
        /// edges (funnels, portals; empty in v1); <paramref name="areaFn"/> an optional override of the
        /// default neighbor-hop area (a Bomb's reach).
        /// </summary>
        public Topology(
            IEnumerable<(CellId Cell, Pos Pos)> cells,
            IEnumerable<(CellId Cell, CellId[] Neighbors)> neighbors,
            IEnumerable<CellLine> lines,
            IEnumerable<(CellId From, CellId To)>? links = null,
            Func<CellId, int, IReadOnlyList<CellId>>? areaFn = null)
        {
            var cellList = new List<(CellId Cell, Pos Pos)>(cells);
            int cap = 0;
            foreach (var c in cellList) cap = Math.Max(cap, c.Cell.Value + 1);
            CellCapacity = cap;
            _pos = new Pos[cap];
            _present = new bool[cap];
            foreach (var (cell, pos) in cellList)
            {
                if (_present[cell.Value]) throw new ArgumentException("duplicate cell " + cell);
                _present[cell.Value] = true;
                _pos[cell.Value] = pos;
                _byPos[(pos.Layer, pos.X, pos.Y)] = cell;
            }
            var sorted = new List<CellId>();
            for (int i = 0; i < cap; i++) if (_present[i]) sorted.Add(new CellId(i));
            Cells = sorted.ToArray();

            _neighbors = new CellId[cap][];
            for (int i = 0; i < cap; i++) _neighbors[i] = new CellId[0];
            foreach (var (cell, nbrs) in neighbors)
                if (cell.Value >= 0 && cell.Value < cap && _present[cell.Value]) _neighbors[cell.Value] = (CellId[])nbrs.Clone();

            Lines = new List<CellLine>(lines).ToArray();
            Links = new List<(CellId From, CellId To)>(links ?? new (CellId, CellId)[0]).ToArray();
            _areaFn = areaFn;

            var through = new List<LineSlot>[cap];
            for (int i = 0; i < cap; i++) through[i] = new List<LineSlot>();
            foreach (var line in Lines)
                for (int i = 0; i < line.Cells.Count; i++)
                    through[line.Cells[i].Value].Add(new LineSlot(line, i));
            _through = new LineSlot[cap][];
            for (int i = 0; i < cap; i++) _through[i] = through[i].ToArray();

            var pairs = new List<(CellId A, CellId B)>();
            foreach (var a in Cells)
                foreach (var b in _neighbors[a.Value])
                    if (a.Value < b.Value) pairs.Add((a, b));
            SwapPairs = pairs.ToArray();
        }

        /// <summary>Every cell, in ascending id order. This is the fixed processing order of the rules.</summary>
        public IReadOnlyList<CellId> Cells { get; }

        /// <summary>One more than the largest cell id: the size of per-cell arrays indexed by <see cref="CellId.Value"/>.</summary>
        public int CellCapacity { get; }

        /// <summary>The match lines (rows and columns, split at holes, on a grid).</summary>
        public IReadOnlyList<CellLine> Lines { get; }

        /// <summary>Directed non-adjacent edges (funnels, pipes, portals, layer drops). Empty in v1.</summary>
        public IReadOnlyList<(CellId From, CellId To)> Links { get; }

        /// <summary>Every neighbor pair (a, b) with a &lt; b, ordered by a then by neighbor order.</summary>
        public IReadOnlyList<(CellId A, CellId B)> SwapPairs { get; }

        /// <summary>True if the id denotes a cell of this topology.</summary>
        public bool Contains(CellId cell) => cell.Value >= 0 && cell.Value < CellCapacity && _present[cell.Value];

        /// <summary>Logical (layer, x, y) coordinates of a cell: for the view only.</summary>
        public Pos Pos(CellId cell) => _pos[cell.Value];

        /// <summary>The cell at logical coordinates, or null if there is none (a hole or off the board).</summary>
        public CellId? CellAt(int x, int y, int layer = 0)
            => _byPos.TryGetValue((layer, x, y), out var c) ? c : (CellId?)null;

        /// <summary>The cells it can swap with.</summary>
        public IReadOnlyList<CellId> Neighbors(CellId cell) => _neighbors[cell.Value];

        /// <summary>Every line through the cell with the cell's index in it, in line order.</summary>
        public IReadOnlyList<LineSlot> LinesThrough(CellId cell) => _through[cell.Value];

        /// <summary>The cells of the line with this axis through <paramref name="cell"/> (what a Line piece clears); just the cell if none.</summary>
        public IReadOnlyList<CellId> LineThrough(CellId cell, int axis)
        {
            foreach (var slot in _through[cell.Value])
                if (slot.Line.Axis == axis) return slot.Line.Cells;
            return new[] { cell };
        }

        /// <summary>The cells a Bomb at <paramref name="cell"/> hits (including the cell), ascending.</summary>
        public IReadOnlyList<CellId> Area(CellId cell, int radius)
        {
            if (_areaFn != null) return _areaFn(cell, radius);
            var seen = new bool[CellCapacity];
            seen[cell.Value] = true;
            var all = new List<CellId> { cell };
            var frontier = new List<CellId> { cell };
            for (int r = 0; r < radius; r++)
            {
                var next = new List<CellId>();
                foreach (var c in frontier)
                    foreach (var n in _neighbors[c.Value])
                        if (!seen[n.Value])
                        {
                            // The frontier is built first and marks it seen afterwards; marking
                            // here only changes duplicates inside one frontier, which are dropped.
                            seen[n.Value] = true;
                            next.Add(n);
                        }
                all.AddRange(next);
                frontier = next;
            }
            all.Sort((a, b) => a.Value.CompareTo(b.Value)); // ids are unique, so the sort is deterministic
            return all;
        }

        /// <summary>
        /// The classic rectangular preset. <c>Y = 0</c> is the top row; cell id = <c>y * w + x</c>.
        /// Holes (given as (x, y)) are absent: no cell, no neighbor, and lines are split at them.
        /// </summary>
        public static Topology Grid(int w, int h, IEnumerable<(int X, int Y)>? holes = null)
        {
            var present = new bool[w * h];
            for (int i = 0; i < present.Length; i++) present[i] = true;
            if (holes != null)
                foreach (var (hx, hy) in holes)
                    if (hx >= 0 && hx < w && hy >= 0 && hy < h) present[hy * w + hx] = false;
            bool Has(int x, int y) => x >= 0 && x < w && y >= 0 && y < h && present[y * w + x];

            var cells = new List<(CellId, Pos)>();
            var neighbors = new List<(CellId, CellId[])>();
            var dirs = new[] { (0, -1), (-1, 0), (1, 0), (0, 1) };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Has(x, y)) continue;
                    var id = new CellId(y * w + x);
                    cells.Add((id, new Pos(0, x, y)));
                    var nb = new List<CellId>();
                    foreach (var (dx, dy) in dirs)
                        if (Has(x + dx, y + dy)) nb.Add(new CellId((y + dy) * w + x + dx));
                    neighbors.Add((id, nb.ToArray()));
                }

            var lines = new List<CellLine>();
            for (int axis = 0; axis < 2; axis++) // 0 = rows, 1 = columns
            {
                int outer = axis == 0 ? h : w, inner = axis == 0 ? w : h;
                for (int o = 0; o < outer; o++)
                {
                    var run = new List<CellId>();
                    for (int i = 0; i < inner; i++)
                    {
                        int x = axis == 0 ? i : o, y = axis == 0 ? o : i;
                        if (Has(x, y)) run.Add(new CellId(y * w + x));
                        else
                        {
                            if (run.Count > 0) lines.Add(new CellLine(axis, run));
                            run = new List<CellId>();
                        }
                    }
                    if (run.Count > 0) lines.Add(new CellLine(axis, run));
                }
            }

            IReadOnlyList<CellId> AreaFn(CellId cell, int radius)
            {
                int cx = cell.Value % w, cy = cell.Value / w;
                var res = new List<CellId>();
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                        if (Has(cx + dx, cy + dy)) res.Add(new CellId((cy + dy) * w + cx + dx));
                res.Sort((a, b) => a.Value.CompareTo(b.Value));
                return res;
            }

            return new Topology(cells, neighbors, lines, null, AreaFn);
        }
    }
}
