using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>The names of the special piece kinds, as used by levels and unlock lists.</summary>
    public static class SpecialKinds
    {
        public const string Line = "Line";
        public const string Bomb = "Bomb";
        public const string Color = "Color";

        /// <summary>All kinds; the default of <see cref="Board.SpecialsEnabled"/>.</summary>
        public static readonly string[] All = { Line, Bomb, Color };
    }

    /// <summary>
    /// A special piece: <see cref="Line"/> (clears a line through its cell), <see cref="Bomb"/>
    /// (clears an area) or <see cref="Color"/> (clears every tile of a color). Immutable values with
    /// structural equality; switch over the nested types.
    /// </summary>
    public abstract record Special
    {
        /// <summary>The kind name used in JSON and unlock lists: "Line", "Bomb" or "Color".</summary>
        public abstract string Kind { get; }

        /// <summary>A Line piece clears <see cref="Topology.LineThrough"/> with this axis (0 = rows, 1 = columns on a grid).</summary>
        public sealed record Line(int Axis) : Special
        {
            public override string Kind => SpecialKinds.Line;
        }

        public sealed record Bomb : Special
        {
            public override string Kind => SpecialKinds.Bomb;
        }

        public sealed record Color : Special
        {
            public override string Kind => SpecialKinds.Color;
        }
    }

    /// <summary>
    /// One tile: a stable <see cref="Id"/> from spawn to clear, a color index and an optional
    /// special. <see cref="Color"/> is -1 only while a spawned tile awaits its color inside <see cref="Flow.Settle"/>.
    /// </summary>
    public sealed record Tile(int Id, int Color, Special? Special = null);

    /// <summary>
    /// Board state: which tile (if any) sits in each cell of a <see cref="Topology"/>, plus the
    /// <see cref="Flow"/>, the color count, the tile id counter and the enabled special kinds.
    /// Mutable; <see cref="Rules.Resolve"/> copies it and never touches its input.
    /// </summary>
    public sealed class Board
    {
        readonly Tile?[] _cells;
        string[] _specials;

        public Board(Topology topology, Flow flow, int colors)
            : this(topology, flow, colors, new Tile?[topology.CellCapacity], 0, SpecialKinds.All) { }

        Board(Topology topology, Flow flow, int colors, Tile?[] cells, int nextTileId, IReadOnlyList<string> specials)
        {
            Topology = topology;
            Flow = flow;
            Colors = colors;
            _cells = cells;
            NextTileId = nextTileId;
            _specials = new List<string>(specials).ToArray();
        }

        /// <summary>An empty board on this topology and flow.</summary>
        public static Board Empty(Topology topology, Flow flow, int colors) => new Board(topology, flow, colors);

        public Topology Topology { get; }
        public Flow Flow { get; }

        /// <summary>Number of tile colors in play (color indices are 0 .. Colors-1).</summary>
        public int Colors { get; }

        /// <summary>The next tile id <see cref="NewTileId"/> will hand out.</summary>
        public int NextTileId { get; private set; }

        /// <summary>
        /// Special kinds ("Line", "Bomb", "Color") that earned qualifiers may turn into pieces
        /// (level unlocks, PRD §4). Earned specials of other kinds are dropped when placing. Kept by <see cref="Copy"/>.
        /// </summary>
        public IReadOnlyList<string> SpecialsEnabled
        {
            get => _specials;
            set => _specials = new List<string>(value).ToArray();
        }

        /// <summary>True if <paramref name="kind"/> is in <see cref="SpecialsEnabled"/>.</summary>
        public bool IsSpecialEnabled(string kind)
        {
            for (int i = 0; i < SpecialsEnabled.Count; i++)
                if (string.Equals(SpecialsEnabled[i], kind, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The legal moves of this board as computed by <see cref="Rules.Resolve"/> (saves a recomputation); any Put/Swap clears it. Never copied.</summary>
        public IReadOnlyList<(CellId A, CellId B)>? LegalCache { get; internal set; }

        /// <summary>The tile in a cell, or null if the cell is empty.</summary>
        public Tile? TileAt(CellId cell) => _cells[cell.Value];

        /// <summary>Puts a tile (or null to empty the cell).</summary>
        public void Put(CellId cell, Tile? tile)
        {
            LegalCache = null;
            _cells[cell.Value] = tile;
        }

        /// <summary>Hands out the next stable tile id.</summary>
        public int NewTileId() => NextTileId++;

        /// <summary>Exchanges the tiles of two cells.</summary>
        public void Swap(CellId a, CellId b)
        {
            LegalCache = null;
            var t = _cells[a.Value];
            _cells[a.Value] = _cells[b.Value];
            _cells[b.Value] = t;
        }

        /// <summary>The cell holding the tile with this id, or null.</summary>
        public CellId? FindTile(int tileId)
        {
            foreach (var cell in Topology.Cells)
            {
                var t = _cells[cell.Value];
                if (t != null && t.Id == tileId) return cell;
            }
            return null;
        }

        /// <summary>Every tile with its cell, in ascending cell id order (what a view draws).</summary>
        public List<(CellId Cell, Tile Tile)> Occupied()
        {
            var list = new List<(CellId, Tile)>();
            foreach (var cell in Topology.Cells)
            {
                var t = _cells[cell.Value];
                if (t != null) list.Add((cell, t));
            }
            return list;
        }

        /// <summary>An independent copy (same topology and flow; tiles are immutable).</summary>
        public Board Copy() => new Board(Topology, Flow, Colors, (Tile?[])_cells.Clone(), NextTileId, SpecialsEnabled);
    }
}
